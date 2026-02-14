using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        // === METAL softening knobs (tudatos, lokális policy) ===
        private const int GlobalWidePenaltyPer01Atr = 3;      // 0.1 ATR-enként
        private const int GlobalWidePenaltyCap = 12;

        private const int SessionWidePenaltyPer01Atr = 4;     // 0.1 ATR-enként
        private const int SessionWidePenaltyCap = 10;
        private const double SessionWideHardFactor = 1.35;    // ha sessionLimit * 1.35 felett: hard reject

        private const int OverextendedPenalty_LondonNy = 7;
        private const int OverextendedPenalty_Asia = 4;

        // =====================================================
        // ENTRY ROUTER (session-dispatch only)
        // =====================================================
        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, "NO_TREND_DIR");

            var session = ctx.Session;

            // XAU FIX: default session = London
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

        // =====================================================
        // CORE (METAL: hard -> soft, score+decision trace)
        // =====================================================
        private EntryEvaluation EvaluateSession(
            EntryContext ctx,
            FxSession session,
            string tag)
        {
            var profile = XAU_InstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Invalid(ctx, "NO_METAL_PROFILE");

            if (profile.FlagTuning == null ||
                !profile.FlagTuning.TryGetValue(session, out var tuning))
                return Invalid(ctx, "NO_FLAG_TUNING");

            int baseScore = tuning.BaseScore;
            int score = baseScore;

            // gyűjtjük az okokat: REJECT esetén is látszik, mi történt
            var reasons = new List<string>(8);
            reasons.Add($"Base={baseScore}");

            // =====================================================
            // IMPULSE (marad hard gate)
            // =====================================================
            if (!ctx.HasImpulse_M5)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "NO_IMPULSE", reasons);

            if (ctx.BarsSinceImpulse_M5 > tuning.MaxBarsSinceImpulse)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "STALE_IMPULSE", reasons);

            // =====================================================
            // FLAG STRUCTURE
            // =====================================================
            if (!TryComputeFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAG_FAIL", reasons);

            // =====================================================
            // XAU IMPULSE EXHAUSTION (HARD TIMING REJECT)
            // =====================================================
            if (ctx.HasImpulse_M5 &&
                IsImpulseExhaustedXau(ctx,
                    lookback: 6,
                    netAtrMult: 1.2,
                    bodyAtrMult: 0.6))
            {
                reasons.Add("IMPULSE_EXHAUSTED");
                return InvalidDecision(
                    ctx,
                    session,
                    tag,
                    score,
                    tuning.MinScore,
                    "IMPULSE_EXHAUSTED",
                    reasons
                );
            }

            // -----------------------------
            // 1) GLOBAL "too wide" -> SOFT penalty (NEM invalid)
            // profile.MaxFlagAtrMult: globális guideline
            // -----------------------------
            if (profile.MaxFlagAtrMult > 0 && rangeAtr > profile.MaxFlagAtrMult)
            {
                int p = ComputeStepPenalty(rangeAtr - profile.MaxFlagAtrMult, GlobalWidePenaltyPer01Atr, GlobalWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_GLOBAL(-{p}) rATR={rangeAtr:F2} lim={profile.MaxFlagAtrMult:F2}");
            }

            // -----------------------------
            // 2) SESSION "too wide" -> penalty, de extrém esetben hard reject
            // tuning.MaxFlagAtrMult: session-specifikus plafon
            // -----------------------------
            double sessionLimit = tuning.MaxFlagAtrMult > 0 ? tuning.MaxFlagAtrMult : profile.MaxFlagAtrMult;

            if (sessionLimit > 0 && rangeAtr > sessionLimit)
            {
                // extrém wide -> hard reject (ritka)
                if (rangeAtr > sessionLimit * SessionWideHardFactor)
                {
                    reasons.Add($"FLAG_WIDE_SESSION_HARD rATR={rangeAtr:F2} lim={sessionLimit:F2}");
                    return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAG_TOO_WIDE_SESSION_HARD", reasons);
                }

                int p = ComputeStepPenalty(rangeAtr - sessionLimit, SessionWidePenaltyPer01Atr, SessionWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_SESSION(-{p}) rATR={rangeAtr:F2} lim={sessionLimit:F2}");
            }

            // =====================================================
            // PULLBACK DEPTH (matrixból, METAL-kompatibilis)
            // =====================================================
            double maxDist = profile.PullbackStyle switch
            {
                FxPullbackStyle.Shallow => tuning.MaxPullbackAtr * 0.8,
                FxPullbackStyle.EMA21 => tuning.MaxPullbackAtr,
                FxPullbackStyle.EMA50 => tuning.MaxPullbackAtr * 1.2,
                FxPullbackStyle.Structure => tuning.MaxPullbackAtr * 1.4,
                _ => tuning.MaxPullbackAtr
            };

            // OVEREXTENDED: METAL soft trendben
            if (IsOverextended(ctx, maxDist))
            {
                bool htfAligned = (ctx.MetalHtfAllowedDirection == TradeDirection.None) ||
                                  (ctx.MetalHtfAllowedDirection == ctx.TrendDirection);

                if (htfAligned)
                {
                    int p = session == FxSession.Asia ? OverextendedPenalty_Asia : OverextendedPenalty_LondonNy;
                    score -= p;
                    reasons.Add($"OVEREXTENDED_SOFT(-{p}) distAtr>{maxDist:F2}");
                }
                else
                {
                    reasons.Add("OVEREXTENDED_COUNTER_HTF");
                    return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "OVEREXTENDED", reasons);
                }
            }

            // =====================================================
            // BREAKOUT (METAL: elfogadjuk M1/structure-t, de logoljuk)
            // =====================================================
            bool m5CloseBreakout = BreakoutClose(ctx, hi, lo, tuning.BreakoutAtrBuffer);
            bool m1Trigger = ctx.M1TriggerInTrendDirection;
            bool flagStructOk = ctx.IsValidFlagStructure_M5;

            // =====================================================
            // XAU EARLY BREAKOUT – SOFT PENALTY (NEM TILT)
            // Erős trendben az első M5 breakout gyakran csak "tap"
            // =====================================================
            if (m5CloseBreakout &&
                !m1Trigger &&
                ctx.BarsSinceImpulse_M5 <= 2 &&
                !ctx.IsAtrExpanding_M5 &&
                !flagStructOk)
            {
                score -= 3; // finom jelzés, NEM tiltás
                reasons.Add("EARLY_BREAKOUT_SOFT(-3)");
            }

            if (!m5CloseBreakout && !m1Trigger && !flagStructOk)
            {
                reasons.Add("NO_BREAKOUT_CLOSE_NO_M1_NO_STRUCT");
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "NO_BREAKOUT_M1_OR_FLAG", reasons);
            }
            else
            {
                // információs jelleggel – látod a logban, mi vitte át
                if (m5CloseBreakout) reasons.Add("BO=M5CLOSE");
                else if (m1Trigger) reasons.Add("BO=M1TRIGGER");
                else if (flagStructOk) reasons.Add("BO=FLAGSTRUCT");
            }

            // =====================================================
            // BODY ALIGNMENT (tuning szerint: hard vagy penalty)
            // =====================================================
            if (!BodyAligned(ctx))
            {
                if (tuning.BodyMisalignPenalty == int.MaxValue)
                {
                    reasons.Add("BODY_MISMATCH_HARD");
                    return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "BODY_MISMATCH", reasons);
                }
                else
                {
                    int p = Math.Max(0, tuning.BodyMisalignPenalty);
                    score -= p;
                    reasons.Add($"BODY_MISMATCH_SOFT(-{p})");
                }
            }

            // =====================================================
            // M1 TRIGGER BONUS / REQUIRE
            // =====================================================
            if (m1Trigger)
            {
                score += tuning.M1TriggerBonus;
                reasons.Add($"+M1({tuning.M1TriggerBonus})");
            }
            else if (tuning.RequireM1Trigger)
            {
                reasons.Add("REQ_M1_TRIGGER_FAIL");
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "NO_M1_TRIGGER", reasons);
            }

            // =====================================================
            // FLAG QUALITY
            // =====================================================
            if (flagStructOk)
            {
                score += tuning.FlagQualityBonus;
                reasons.Add($"+FQ({tuning.FlagQualityBonus})");
            }

            // =====================================================
            // HTF PENALTY (METAL)
            // =====================================================
            int htfP = HtfPenalty(ctx, ctx.TrendDirection, tuning.HtfBasePenalty, tuning.HtfScalePenalty);
            if (htfP > 0)
            {
                score -= htfP;
                reasons.Add($"-HTF({htfP})");
            }

            // Router kompatibilitás
            if (score > 0 && score < 20)
                score = 20;

            // MinScore gate (marad)
            if (score < tuning.MinScore)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, $"LOW_SCORE({score})", reasons);

            return ValidDecision(ctx, session, tag, score, tuning.MinScore, rangeAtr, reasons);
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private static int ComputeStepPenalty(double diffAtr, int per01, int cap)
        {
            if (diffAtr <= 0) return 0;
            // 0.10 ATR lépcsők
            int steps = (int)Math.Ceiling(diffAtr / 0.10);
            int p = steps * per01;
            if (p > cap) p = cap;
            return p;
        }

        private static bool TryComputeFlag(
            EntryContext ctx,
            int flagBars,
            out double hi,
            out double lo,
            out double rangeAtr)
        {
            hi = double.MinValue;
            lo = double.MaxValue;
            rangeAtr = 999;

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int end = lastClosed - 1;
            int start = end - flagBars + 1;
            if (start < 2) return false;

            for (int i = start; i <= end; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            if (ctx.AtrM5 <= 0)
                return false;

            rangeAtr = (hi - lo) / ctx.AtrM5;
            return true;
        }

        private static bool IsOverextended(EntryContext ctx, double maxDistAtr)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;
            double dist = Math.Abs(bars[i].Close - ctx.Ema21_M5);
            return dist > ctx.AtrM5 * maxDistAtr;
        }

        private static bool BreakoutClose(
            EntryContext ctx,
            double hi,
            double lo,
            double bufAtr)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;
            double close = bars[i].Close;
            double buf = ctx.AtrM5 * bufAtr;

            if (ctx.TrendDirection == TradeDirection.Long)
                return close > hi + buf;

            if (ctx.TrendDirection == TradeDirection.Short)
                return close < lo - buf;

            return false;
        }

        private static bool BodyAligned(EntryContext ctx)
        {
            var bars = ctx.M5;
            int i = bars.Count - 2;

            if (ctx.TrendDirection == TradeDirection.Long)
                return bars[i].Close > bars[i].Open;

            if (ctx.TrendDirection == TradeDirection.Short)
                return bars[i].Close < bars[i].Open;

            return false;
        }

        private static EntryEvaluation ValidDecision(
            EntryContext ctx,
            FxSession session,
            string tag,
            int score,
            int minScore,
            double rangeAtr,
            List<string> reasons)
        {
            // egy sorban logolható decision string
            string note = $"[{tag}] {ctx.Symbol} {session} dir={ctx.TrendDirection} " +
                          $"Score={score} Min={minScore} RangeATR={rangeAtr:F2} " +
                          $"Decision=ACCEPT | " + string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = ctx.TrendDirection,
                Score = score,
                IsValid = true,
                Reason = note
            };
        }

        private static EntryEvaluation InvalidDecision(
            EntryContext ctx,
            FxSession session,
            string tag,
            int score,
            int minScore,
            string reason,
            List<string> reasons)
        {
            string note = $"[{tag}] {ctx?.Symbol} {session} dir={ctx?.TrendDirection} " +
                          $"Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
                          (reasons != null ? string.Join(" | ", reasons) : "");

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

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };

        // =====================================================
        // HTF PENALTY HELPER (METAL – FX mintára)
        // =====================================================
        private static int HtfPenalty(
            EntryContext ctx,
            TradeDirection dir,
            int basePenalty,
            int scalePenalty)
        {
            if (ctx.MetalHtfAllowedDirection != TradeDirection.None &&
                ctx.MetalHtfConfidence01 > 0 &&
                ctx.MetalHtfAllowedDirection != dir)
            {
                return (int)(basePenalty + scalePenalty * ctx.MetalHtfConfidence01);
            }

            return 0;
        }

        private static bool IsImpulseExhaustedXau(EntryContext ctx, int lookback, double netAtrMult, double bodyAtrMult)
        {
            var bars = ctx.M5;
            if (bars.Count < lookback + 2 || ctx.AtrM5 <= 0)
                return false;

            int end = bars.Count - 2;
            int start = end - lookback;

            double startClose = bars[start].Close;
            double endClose = bars[end].Close;
            double netMove = Math.Abs(endClose - startClose);

            double body = Math.Abs(bars[end].Close - bars[end].Open);

            bool netTooLarge = netMove > ctx.AtrM5 * netAtrMult;
            bool bodyLarge = body > ctx.AtrM5 * bodyAtrMult;

            return netTooLarge && bodyLarge;
        }
    }
}
