using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    /// <summary>
    /// XAU FlagEntry V2 (B-mode): 2-sided eval (BUY+SELL), HTF-against allowed with penalty.
    /// Stable, fewer trades: Impulse required, stale impulse hard reject, breakout-only trigger,
    /// ATR-normalized flag width guard, body dominance, simple structure sanity.
    /// </summary>
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        // ===== simple, stable knobs =====
        private const int HtfAgainstPenalty = 6;          // score penalty only (NO hard block)
        private const double MinBodyRatio = 0.55;         // body dominance floor (body/range)
        private const int BarsNotReadyMin = 20;

        // Optional global/session wide penalties kept minimal (use your matrix if exists)
        private const int GlobalWidePenaltyPer01Atr = 2;
        private const int GlobalWidePenaltyCap = 8;

        private const int SessionWidePenaltyPer01Atr = 4;
        private const int SessionWidePenaltyCap = 10;
        private const double SessionWideHardFactor = 1.6;

        private const string VersionTag = "XAU_FLAG_V2B";

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < BarsNotReadyMin)
                return Invalid(ctx, "CTX_NOT_READY");

            var session = ctx.Session;
            if ((int)session == 0)
                session = FxSession.London;

            return session switch
            {
                FxSession.Asia => EvaluateSession(ctx, FxSession.Asia, "XAU_FLAG_ASIA"),
                FxSession.London => EvaluateSession(ctx, FxSession.London, "XAU_FLAG_LONDON"),
                FxSession.NewYork => EvaluateSession(ctx, FxSession.NewYork, "XAU_FLAG_NY"),
                _ => Invalid(ctx, "NO_SESSION")
            };
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

            // ===== evaluate both sides (HTF against allowed) =====
            var buyEval = EvaluateSide(TradeDirection.Long, ctx, session, tag, tuning, profile, hi, lo, rangeAtr, lastBar, last, baseReasons);
            var sellEval = EvaluateSide(TradeDirection.Short, ctx, session, tag, tuning, profile, hi, lo, rangeAtr, lastBar, last, baseReasons);

            // ===== decision: prefer valid; if both valid pick higher score; if none valid pick higher score (for debug usefulness) =====
            if (buyEval.IsValid && !sellEval.IsValid) return buyEval;
            if (!buyEval.IsValid && sellEval.IsValid) return sellEval;

            return buyEval.Score >= sellEval.Score ? buyEval : sellEval;
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
            var reasons = new List<string>(baseReasons.Count + 16);
            reasons.AddRange(baseReasons);

            // ===== wide-flag guard (keeps out garbage ranges) =====
            // global
            if ((double)profile.MaxFlagAtrMult > 0 && rangeAtr > (double)profile.MaxFlagAtrMult)
            {
                int p = ComputeStepPenalty(rangeAtr - (double)profile.MaxFlagAtrMult, GlobalWidePenaltyPer01Atr, GlobalWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_GLOBAL(-{p}) rangeAtr={rangeAtr:F2} lim={((double)profile.MaxFlagAtrMult):F2}");
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

            // ===== breakout trigger (B-mode): closeBreak OR wickBreak+strongBody =====
            double lastClose = lastBar.Close;
            double lastOpen = lastBar.Open;
            double lastHigh = lastBar.High;
            double lastLow = lastBar.Low;

            bool closeBreak = dir == TradeDirection.Long ? lastClose > hi : lastClose < lo;
            bool wickBreak = dir == TradeDirection.Long ? lastHigh >= hi : lastLow <= lo;

            bool strongBodyDir = dir == TradeDirection.Long ? lastClose > lastOpen : lastClose < lastOpen;

            double range = lastHigh - lastLow;
            double body = Math.Abs(lastClose - lastOpen);
            double bodyRatio = range > 0 ? body / range : 0;

            bool bodyDominant = bodyRatio >= MinBodyRatio;

            bool breakout = closeBreak || (wickBreak && strongBodyDir);

            reasons.Add($"DBG_SIDE dir={dir} hi={hi:F2} lo={lo:F2} close={lastClose:F2} open={lastOpen:F2} high={lastHigh:F2} low={lastLow:F2}");
            reasons.Add($"DBG_BREAK closeBreak={closeBreak} wickBreak={wickBreak} strongBodyDir={strongBodyDir} bodyRatio={bodyRatio:F2} bodyDom>={MinBodyRatio:F2}={bodyDominant}");

            if (!breakout)
                return InvalidDecisionDir(ctx, session, tag, score, minScore, dir, "NO_BREAK", reasons);

            if (!bodyDominant)
            {
                score -= 4;
                reasons.Add("WEAK_BODY(-4)");
            }

            // ===== HTF bias handling: penalty only =====
            // ctx.TrendDirection = HTF bias proxy in your system
            if (ctx.TrendDirection != TradeDirection.None && dir != ctx.TrendDirection)
            {
                score -= HtfAgainstPenalty;
                reasons.Add($"HTF_AGAINST(-{HtfAgainstPenalty}) htf={ctx.TrendDirection}");
            }
            else
            {
                reasons.Add($"HTF_OK htf={ctx.TrendDirection}");
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
            catch { /* ignore if field not present */ }

            if (score < minScore)
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

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int start = lastClosed - flagBars + 1;
            if (start < 2) return false;

            for (int i = start; i <= lastClosed; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            if (ctx.AtrM5 <= 0)
                return false;

            rangeAtr = (hi - lo) / ctx.AtrM5;
            return true;
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
                Direction = ctx?.TrendDirection ?? TradeDirection.None,
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