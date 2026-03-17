using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    /// <summary>
    /// XAU FlagEntry V2 (B-mode):
    /// - 2-sided eval (BUY+SELL)
    /// - HTF-against allowed with SMALL penalty
    /// - HTF-against requires STRONGER breakout quality (closeBreak + ATR margin + slightly higher body ratio)
    /// - Spike/ambiguous wick-break both sides is rejected (prevents "felül short / alul long" noise)
    /// - Decision: prefer valid; if both valid choose best (soft HTF preference only in small score band);
    ///            if none valid -> return aggregated invalid (Direction=None) for clean logs.
    /// </summary>
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        // ===== simple, stable knobs =====
        private const int HtfAgainstPenalty = 6;          // small score penalty only (NO hard block)
        private const double MinBodyRatio = 0.55;         // body dominance floor (body/range)
        private const int BarsNotReadyMin = 20;
        private const int MaxFlagBarsHardLimit = 50;

        // HTF-against quality requirements (NOT huge penalty; stronger definition)
        private const double HtfAgainstMinCloseBreakAtr = 0.08; // close must be this far beyond hi/lo in ATR units
        private const double HtfAgainstMinBodyRatio = 0.60;     // slightly stricter than MinBodyRatio

        // Ambiguous spike guard: wick breaks BOTH sides but no close break
        private const bool RejectAmbiguousWickBothSides = true;

        // Soft preference when both sides valid and scores are close
        private const int BothValidScoreDeadband = 2;

        // Optional global/session wide penalties kept minimal (use your matrix if exists)
        private const int GlobalWidePenaltyPer01Atr = 2;
        private const int GlobalWidePenaltyCap = 8;

        private const int SessionWidePenaltyPer01Atr = 4;
        private const int SessionWidePenaltyCap = 10;
        private const double SessionWideHardFactor = 1.6;
        private const int NoBreakoutPenalty = 4;

        private const string VersionTag = "XAU_FLAG_V2B_STRONGHTF";

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Invalid(ctx, "SESSION_MATRIX_FLAG_DISABLED");
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < BarsNotReadyMin)
                return Invalid(ctx, "CTX_NOT_READY");

            var session = ctx.Session;
            ctx.Log?.Invoke($"[XAU_FLAG][SESSION_CTX] rawBucket={session}");

            if (!TryResolveSessionProfile(session, out var resolvedSession, out var tag))
                return Invalid(ctx, "NO_SESSION");

            ctx.Log?.Invoke($"[XAU_FLAG][SESSION_PROFILE] resolvedProfile={resolvedSession}");
            return EvaluateSession(ctx, resolvedSession, tag);
        }

        private EntryEvaluation EvaluateSession(EntryContext ctx, FxSession session, string tag)
        {
            var profile = XAU_InstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Invalid(ctx, "NO_METAL_PROFILE");

            if (profile.FlagTuning == null || !profile.FlagTuning.TryGetValue(session, out var tuning))
                return Invalid(ctx, "NO_FLAG_TUNING");

            // ===== base reasons shared =====
            var baseReasons = new List<string>(32)
            {
                VersionTag,
                $"Base={tuning.BaseScore}"
            };

            // ===== must have impulse (stability gate) =====
            if (!ctx.HasImpulse_M5)
                return InvalidDecision(ctx, session, tag, tuning.BaseScore, tuning.MinScore, "NO_IMPULSE", baseReasons);

            if (ctx.BarsSinceImpulse_M5 > tuning.MaxBarsSinceImpulse)
                return InvalidDecision(ctx, session, tag, tuning.BaseScore, tuning.MinScore, "STALE_IMPULSE", baseReasons);

            // ===== compute flag hi/lo + ATR normalized width =====
            if (!TryComputeFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return InvalidDecision(ctx, session, tag, tuning.BaseScore, tuning.MinScore, "FLAG_FAIL", baseReasons);

            // ===== last closed bar =====
            var bars = ctx.M5;
            int last = bars.Count - 2;
            var lastBar = bars[last];

            // ===== optional ambiguity guard at session-level (cheap early exit) =====
            // If the candle wicked both sides but did not close out, it's likely a spike, not a clean flag breakout.
            if (RejectAmbiguousWickBothSides)
            {
                bool wickBoth = lastBar.High >= hi && lastBar.Low <= lo;
                bool closeUp = lastBar.Close > hi;
                bool closeDn = lastBar.Close < lo;

                if (wickBoth && !closeUp && !closeDn)
                {
                    var reasons = new List<string>(baseReasons.Count + 8);
                    reasons.AddRange(baseReasons);
                    reasons.Add($"DBG_AMBIG wickBoth=True hi={hi:F2} lo={lo:F2} close={lastBar.Close:F2} high={lastBar.High:F2} low={lastBar.Low:F2}");
                    return InvalidDecision(ctx, session, tag, tuning.BaseScore, tuning.MinScore, "AMBIGUOUS_SPIKE_WICKS", reasons);
                }
            }

            // ===== evaluate both sides (HTF against allowed) =====
            var buyEval = EvaluateSide(TradeDirection.Long, ctx, session, tag, tuning, profile, hi, lo, rangeAtr, lastBar, last, baseReasons);
            var sellEval = EvaluateSide(TradeDirection.Short, ctx, session, tag, tuning, profile, hi, lo, rangeAtr, lastBar, last, baseReasons);
            ctx.Log?.Invoke($"[XAU_FLAG][SIDE_CHECK] buyValid={buyEval.IsValid} sellValid={sellEval.IsValid}");

            if (!buyEval.IsValid && !sellEval.IsValid)
            {
                ctx.Log?.Invoke($"[FLAG][REJECT] No valid direction buyValid={buyEval.IsValid} sellValid={sellEval.IsValid}");

                return InvalidDecisionNone(
                    ctx, session, tag,
                    tuning.BaseScore, tuning.MinScore,
                    "FLAG_DIRECTION_INVALID",
                    baseReasons,
                    buyEval,
                    sellEval
                );
            }

            // ===== decision =====
            if (buyEval.IsValid && !sellEval.IsValid) return buyEval;
            if (!buyEval.IsValid && sellEval.IsValid) return sellEval;

            if (buyEval.IsValid && sellEval.IsValid)
            {
                // If scores are close, softly prefer HTF-aligned direction (NOT a block, not a huge penalty).
                int diff = buyEval.Score - sellEval.Score;

                bool buyAligned = (ctx.TrendDirection == TradeDirection.None) || (buyEval.Direction == ctx.TrendDirection);
                bool sellAligned = (ctx.TrendDirection == TradeDirection.None) || (sellEval.Direction == ctx.TrendDirection);

                if (Math.Abs(diff) <= BothValidScoreDeadband)
                {
                    if (buyAligned && !sellAligned) return buyEval;
                    if (!buyAligned && sellAligned) return sellEval;
                }

                return diff >= 0 ? buyEval : sellEval;
            }

            int fallbackScore = Math.Max((int)tuning.BaseScore, Math.Max(buyEval.Score, sellEval.Score));
            int minScore = (int)tuning.MinScore;
            var bias = ResolveHtfBias(ctx);
            ctx.Log?.Invoke($"[XAU_FLAG][FALLBACK_CHECK] score={fallbackScore} min={minScore} bias={bias}");

            if (fallbackScore >= minScore)
            {
                if (bias == TradeDirection.Short || bias == TradeDirection.Long)
                {
                    ctx.Log?.Invoke($"[XAU_FLAG][FALLBACK_DIRECTION] using HTF bias {bias}");
                    ctx.Log?.Invoke($"[XAU_FLAG][FALLBACK_DIRECTION] bias={bias} score={fallbackScore}");

                    var reasons = new List<string>(baseReasons.Count + 8);
                    reasons.AddRange(baseReasons);
                    reasons.Add($"BUY: valid={buyEval.IsValid} score={buyEval.Score}");
                    reasons.Add($"SELL: valid={sellEval.IsValid} score={sellEval.Score}");
                    reasons.Add(bias == TradeDirection.Short ? "HTF_BIAS_FALLBACK_SELL" : "HTF_BIAS_FALLBACK_BUY");

                    return ValidDecisionDir(ctx, session, tag, fallbackScore, minScore, bias, rangeAtr, reasons);
                }
            }

            // none valid -> aggregated invalid for clean logs (Direction=None)
            return InvalidDecisionNone(
                ctx, session, tag,
                tuning.BaseScore, tuning.MinScore,
                "NO_VALID_SIDE",
                baseReasons,
                buyEval,
                sellEval
            );
        }

        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            FxSession session,
            string tag,
            dynamic tuning,
            dynamic profile,
            double hi,
            double lo,
            double rangeAtr,
            Bar lastBar,
            int lastIndex,
            List<string> baseReasons)
        {
            int score = (int)tuning.BaseScore;
            int minScore = (int)tuning.MinScore;

            // copy base reasons
            var reasons = new List<string>(baseReasons.Count + 24);
            reasons.AddRange(baseReasons);

            // ===== wide-flag guard (keeps out garbage ranges) =====
            // global
            if ((double)profile.MaxFlagAtrMult > 0 && rangeAtr > (double)profile.MaxFlagAtrMult)
            {
                int p = ComputeStepPenalty(rangeAtr - (double)profile.MaxFlagAtrMult, GlobalWidePenaltyPer01Atr, GlobalWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_GLOBAL(-{p}) rangeAtr={rangeAtr:F2} lim={((double)profile.MaxFlagAtrMult):F2}");
            }

            if (rangeAtr > 1.2)
            {
                score -= 5;
                reasons.Add("WIDE_FLAG(-5)");
            }
            
            // session
            double sessionLimit = (double)tuning.MaxFlagAtrMult > 0 ? (double)tuning.MaxFlagAtrMult : (double)profile.MaxFlagAtrMult;
            if (sessionLimit > 0 && rangeAtr > sessionLimit)
            {
                if (rangeAtr > sessionLimit * SessionWideHardFactor)
                    return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "FLAG_TOO_WIDE_HARD", reasons);

                int p = ComputeStepPenalty(rangeAtr - sessionLimit, SessionWidePenaltyPer01Atr, SessionWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_SESSION(-{p}) rangeAtr={rangeAtr:F2} lim={sessionLimit:F2}");
            }

            // breakout trigger (strict mode): closeBreak + ATR margin
            // wickBreak ignored to avoid XAU spike fakeouts
            double lastClose = lastBar.Close;
            double lastOpen = lastBar.Open;
            double lastHigh = lastBar.High;
            double lastLow = lastBar.Low;

            bool closeBreak = dir == TradeDirection.Long ? lastClose > hi : lastClose < lo;

            /*bool wickBreak = dir == TradeDirection.Long
                ? lastHigh > hi
                : lastLow < lo;

            bool strongBodyDir = dir == TradeDirection.Long ? lastClose > lastOpen : lastClose < lastOpen;
*/

            double candleRange = lastHigh - lastLow;
            double body = Math.Abs(lastClose - lastOpen);
            double bodyRatio = candleRange > 0 ? body / candleRange : 0;
            
            bool bodyDominant = bodyRatio >= MinBodyRatio;

            // distance beyond breakout boundary (for HTF-against quality)
            // breakout trigger (strict mode)
            double breakDist = dir == TradeDirection.Long
                ? Math.Max(0, lastClose - hi)
                : Math.Max(0, lo - lastClose);

            double breakAtr = (ctx.AtrM5 > 0) ? (breakDist / ctx.AtrM5) : 0;

            const double MinCloseBreakAtr = 0.05;

            bool breakout =
                closeBreak
                && breakAtr >= MinCloseBreakAtr
                && bodyDominant;

            bool isHtfAgainst = (ctx.TrendDirection != TradeDirection.None && dir != ctx.TrendDirection);
            bool htfBiasAligned = ctx.MetalHtfAllowedDirection == TradeDirection.None || ctx.MetalHtfAllowedDirection == dir;
            bool trendAligned = ctx.MarketState?.IsTrend != false;

            reasons.Add($"DBG_SIDE dir={dir} hi={hi:F2} lo={lo:F2} close={lastClose:F2} open={lastOpen:F2} high={lastHigh:F2} low={lastLow:F2}");
            reasons.Add($"DBG_BREAK closeBreak={closeBreak} bodyRatio={bodyRatio:F2} bodyDom>={MinBodyRatio:F2}={bodyDominant} breakAtr={breakAtr:F2} htf={ctx.TrendDirection} htfAgainst={isHtfAgainst}");
            reasons.Add($"DBG_ALIGN htfBias={ctx.MetalHtfAllowedDirection} aligned={htfBiasAligned} trend={trendAligned}");

            if (breakout)
            {
                reasons.Add("BREAKOUT_OK");
            }
            else
            {
                if (bodyRatio < 0.30)
                {
                    score -= 6;
                    reasons.Add("WEAK_BODY_BREAK(-6)");
                }
                else
                {
                    score -= 3;
                    reasons.Add("NO_BREAKOUT(-3)");
                }
            }

            if (!htfBiasAligned)
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "HTF_BIAS_MISMATCH", reasons);

            if (!trendAligned)
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "TREND_NOT_ALIGNED", reasons);

            if (!ctx.IsValidFlagStructure_M5)
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "FLAG_STRUCTURE_INVALID", reasons);

            if (isHtfAgainst)
            {
                bool weakAgainst =
                    !closeBreak ||
                    breakAtr < HtfAgainstMinCloseBreakAtr * 0.7;

                if (weakAgainst)
                {
                    score -= 8;
                    reasons.Add("HTF_AGAINST_WEAK(-8)");
                }

                if (bodyRatio < HtfAgainstMinBodyRatio)
                {
                    score -= 3;
                    reasons.Add("HTF_AGAINST_WEAK_BODY(-3)");
                }

                // EXTRA: ha breakout sincs ÉS body is gyenge → kill
                if (!closeBreak && bodyRatio < 0.4)
                {
                    return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "HTF_AGAINST_NO_QUALITY", reasons);
                }
            }
            else
            {
                reasons.Add("HTF_OK");
            }

            // ===== basic structure sanity (cheap anti-stupidity) =====
            if (dir == TradeDirection.Long && IsLowerHighSequence(ctx.M5, lastIndex))
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "LOWER_HIGH_SEQ", reasons);

            if (dir == TradeDirection.Short && IsHigherLowSequence(ctx.M5, lastIndex))
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "HIGHER_LOW_SEQ", reasons);

            // ===== optional quality bonus (if exists in tuning/profile) =====
            try
            {
                if (ctx.IsValidFlagStructure_M5)
                {
                    score += (int)tuning.FlagQualityBonus;
                    reasons.Add($"+FQ({(int)tuning.FlagQualityBonus})");
                }
            }
            catch
            {
                // ignore if field not present
            }

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            if (missingImpulse)
            {
                score = Math.Max(0, score - 6);

                ctx.Log?.Invoke(
                    "[FLAG][PENALTY] Missing impulse detected → score penalty applied " +
                    $"symbol={ctx.Symbol} entry={EntryType.XAU_Flag} penalty=6 score={score}");
            }

            bool nearThreshold = score >= (minScore - 3);
            bool strongContext =
                ctx.MarketState?.IsTrend == true &&
                ctx.MarketState?.Adx > 30.0 &&
                (ctx.MetalHtfAllowedDirection == TradeDirection.None || ctx.MetalHtfAllowedDirection == dir);

            bool valid = score >= minScore || (nearThreshold && strongContext);

            reasons.Add($"[FLAG][THRESHOLD_CHECK] score={score} threshold={minScore} near={nearThreshold.ToString().ToLowerInvariant()} context={strongContext.ToString().ToLowerInvariant()} valid={valid.ToString().ToLowerInvariant()}");

            if (!valid)
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "LOW_SCORE", reasons);

            reasons.Add("ACCEPT");
            return ValidDecisionDir(ctx, session, tag, score, minScore, dir, rangeAtr, reasons);
        }

        // ===============================
        // Helpers
        // ===============================

        private static bool TryComputeFlag(EntryContext ctx, int flagBars, out double hi, out double lo, out double rangeAtr)
        {
            hi = double.MinValue;
            lo = double.MaxValue;
            rangeAtr = 999;

            if (flagBars > MaxFlagBarsHardLimit)
            {
                ctx?.Log?.Invoke("[FLAG][ERROR] invalid bar count");
                return false;
            }

            var bars = ctx.M5;

            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;
            int start = flagEnd - flagBars + 1;

            if (start < 2)
                return false;

            for (int i = start; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            if (ctx.AtrM5 <= 0)
                return false;

            double range = hi - lo;
            if (range <= 0)
                return false;

            rangeAtr = range / ctx.AtrM5;
            ctx.Log?.Invoke($"[FLAG][BARCOUNT] bars={flagBars} range={range:0.00} compression={rangeAtr:0.00}");
            return true;
        }


        private static bool TryResolveSessionProfile(FxSession rawSession, out FxSession resolvedSession, out string tag)
        {
            resolvedSession = rawSession;
            tag = rawSession switch
            {
                FxSession.Asia => "XAU_FLAG_ASIA",
                FxSession.London => "XAU_FLAG_LONDON",
                FxSession.NewYork => "XAU_FLAG_NEWYORK",
                _ => null
            };

            return tag != null;
        }

        private static int ComputeStepPenalty(double diffAtr, int per01, int cap)
        {
            if (diffAtr <= 0) return 0;
            int steps = (int)Math.Ceiling(diffAtr / 0.10);
            int p = steps * per01;
            return Math.Min(p, cap);
        }

        private static bool IsLowerHighSequence(Bars bars, int last)
        {
            int h1 = last - 1, h2 = last - 2, h3 = last - 3;
            if (h3 < 0) return false;
            return bars[h1].High < bars[h2].High && bars[h2].High < bars[h3].High;
        }

        private static bool IsHigherLowSequence(Bars bars, int last)
        {
            int l1 = last - 1, l2 = last - 2, l3 = last - 3;
            if (l3 < 0) return false;
            return bars[l1].Low > bars[l2].Low && bars[l2].Low > bars[l3].Low;
        }

        private static TradeDirection ResolveHtfBias(EntryContext ctx)
        {
            if (ctx == null)
                return TradeDirection.None;

            if (ctx.MetalHtfAllowedDirection == TradeDirection.Long || ctx.MetalHtfAllowedDirection == TradeDirection.Short)
                return ctx.MetalHtfAllowedDirection;

            if (ctx.TrendDirection == TradeDirection.Long || ctx.TrendDirection == TradeDirection.Short)
                return ctx.TrendDirection;

            return TradeDirection.None;
        }

        private static EntryEvaluation InvalidDecision(
            EntryContext ctx, FxSession session, string tag,
            int score, int minScore, string reason, List<string> reasons)
        {
            string note =
                $"[{tag}] {ctx?.Symbol} {session} Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }

        private static EntryEvaluation InvalidDecisionNone(
            EntryContext ctx, FxSession session, string tag,
            int score, int minScore,
            string reason,
            List<string> baseReasons,
            EntryEvaluation buyEval,
            EntryEvaluation sellEval)
        {
            var reasons = new List<string>(baseReasons.Count + 8);
            reasons.AddRange(baseReasons);

            // Keep it readable: just summarize outcomes, do not embed full long notes twice.
            reasons.Add($"BUY: valid={buyEval.IsValid} score={buyEval.Score}");
            reasons.Add($"SELL: valid={sellEval.IsValid} score={sellEval.Score}");

            string note =
                $"[{tag}] {ctx?.Symbol} {session} Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }

        private static EntryEvaluation InvalidDecisionDir(
            EntryContext ctx, FxSession session, string tag,
            int score, int minScore, TradeDirection dir, string reason, List<string> reasons)
        {
            string note =
                $"[{tag}] {ctx?.Symbol} {session} dir={dir} htf={ctx?.TrendDirection} Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }

        private static EntryEvaluation ValidDecisionDir(
            EntryContext ctx, FxSession session, string tag,
            int score, int minScore, TradeDirection dir, double rangeAtr, List<string> reasons)
        {
            string note =
                $"[{tag}] {ctx.Symbol} {session} dir={dir} htf={ctx.TrendDirection} Score={score} Min={minScore} RangeATR={rangeAtr:F2} Decision=ACCEPT | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = note
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
    }
}
