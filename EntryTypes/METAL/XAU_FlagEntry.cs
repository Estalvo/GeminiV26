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

        private const int GlobalWidePenaltyPer01Atr = 2;
        private const int GlobalWidePenaltyCap = 8;

        private const int SessionWidePenaltyPer01Atr = 4;
        private const int SessionWidePenaltyCap = 10;
        private const double SessionWideHardFactor = 1.6;

        private const int OverextendedPenalty_LondonNy = 5;
        private const int OverextendedPenalty_Asia = 3;

        // ==========================================================
        // METAL LATE FILTER v1 (ADD-ONLY) – VERSIONED KNOBS
        // ==========================================================
        private const string LateFilterVersion = "METAL_LATE_FILTER_V1";

        // Hard block only on true climax roll (rare, but deadly if not blocked)
        private const double XauAdxClimax = 42.0;
        private const double XagAdxClimax = 38.0;
        private const double DefaultAdxClimax = 40.0;

        private const int ClimaxMinBarsSinceImpulse = 2;

        // Soft penalties / minScore boost
        private const int LateBreakSoftPenalty_London = 6;
        private const int LateBreakSoftPenalty_NewYork = 8;
        private const int LateBreakHardExtraPenalty_NY = 4; // only when very late + no signal

        private const int TransitionMinScoreBoost = 5; // “HTF-transition proxy” (no new ctx props needed)

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, "NO_TREND_DIR");

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

            int score = tuning.BaseScore;
            var reasons = new List<string>(16);
            reasons.Add($"Base={tuning.BaseScore}");
            reasons.Add(LateFilterVersion);

            if (!ctx.HasImpulse_M5)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "NO_IMPULSE", reasons);

            if (ctx.BarsSinceImpulse_M5 > tuning.MaxBarsSinceImpulse)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "STALE_IMPULSE", reasons);

            if (!TryComputeFlag(ctx, tuning.FlagBars, out var hi, out var lo, out var rangeAtr))
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAG_FAIL", reasons);

            if (IsImpulseExhaustedXau(ctx, 6, 1.4, 0.6))
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "IMPULSE_EXHAUSTED", reasons);

            // ===============================
            // TREND FATIGUE (EXISTING)
            // ===============================
            bool trendFatigue =
                ctx.Adx_M5 > 40 &&
                ctx.AdxSlope_M5 <= 0 &&
                ctx.AtrSlope_M5 <= 0 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 6 &&
                ctx.BarsSinceImpulse_M5 > 3;

            if (trendFatigue)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "TREND_FATIGUE", reasons);

            // ===============================
            // CONTINUATION STRENGTH FLOOR (NEW)
            // ===============================
            if (ctx.Adx_M5 < 23)
            {
                reasons.Add($"ADX_TOO_LOW({ctx.Adx_M5:F1}<23)");
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "WEAK_TREND_ADX", reasons);
            }

            // ==========================================================
            // METAL LATE FILTER v1 – 1) CLIMAX ROLL HARD BLOCK (ADD-ONLY)
            // ==========================================================
            // Goal: block only the "climax then roll" continuation entries that very often go full loss.
            // Matrix-aware: XAU higher climax threshold than XAG.
            double adxClimax = ResolveAdxClimax(profile);

            bool climaxRoll =
                ctx.Adx_M5 >= adxClimax &&
                ctx.AdxSlope_M5 <= 0 &&
                ctx.BarsSinceImpulse_M5 >= ClimaxMinBarsSinceImpulse &&
                !ctx.IsAtrExpanding_M5; // expanding ATR can still be a valid continuation in metals

            if (climaxRoll)
            {
                reasons.Add($"CLIMAX_ROLL_BLOCK(adx={ctx.Adx_M5:F1}>=thr{adxClimax:F0} slope={ctx.AdxSlope_M5:F3} bsi={ctx.BarsSinceImpulse_M5} atrExp={ctx.IsAtrExpanding_M5})");
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "CLIMAX_ROLL", reasons);
            }

            // ===============================
            // WIDE FLAG penalties (EXISTING)
            // ===============================
            if (profile.MaxFlagAtrMult > 0 && rangeAtr > profile.MaxFlagAtrMult)
            {
                int p = ComputeStepPenalty(rangeAtr - profile.MaxFlagAtrMult, GlobalWidePenaltyPer01Atr, GlobalWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_GLOBAL(-{p})");
            }

            double sessionLimit = tuning.MaxFlagAtrMult > 0
                ? tuning.MaxFlagAtrMult
                : profile.MaxFlagAtrMult;

            if (sessionLimit > 0 && rangeAtr > sessionLimit)
            {
                if (rangeAtr > sessionLimit * SessionWideHardFactor)
                    return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAG_TOO_WIDE_HARD", reasons);

                int p = ComputeStepPenalty(rangeAtr - sessionLimit, SessionWidePenaltyPer01Atr, SessionWidePenaltyCap);
                score -= p;
                reasons.Add($"FLAG_WIDE_SESSION(-{p})");
            }

            // ===============================
            // BREAKOUT / ENERGY (EXISTING)
            // ===============================
            bool m5Break = BreakoutClose(ctx, hi, lo, tuning.BreakoutAtrBuffer);
            bool m1 = ctx.M1TriggerInTrendDirection;
            bool flagStruct = ctx.IsValidFlagStructure_M5;

            bool energyOk = m5Break || m1 || ctx.IsAtrExpanding_M5;

            if (!m5Break && !m1 && !flagStruct)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "NO_BREAKOUT_SIGNAL", reasons);

            if (flagStruct && !energyOk)
                return InvalidDecision(ctx, session, tag, score, tuning.MinScore, "FLAGSTRUCT_NO_ENERGY", reasons);

            // ==========================================================
            // METAL LATE FILTER v1 – 2) LATE CONTINUATION PENALTY (ADD-ONLY)
            // ==========================================================
            // Goal: if structure break is not fresh AND no breakout / no M1 confirmation,
            // penalize heavily (NY stronger), but do NOT fully block -> not "no trading".
            int barsSinceBreak =
                ctx.TrendDirection == TradeDirection.Long
                    ? ctx.BarsSinceHighBreak_M5
                    : ctx.BarsSinceLowBreak_M5;

            bool noFreshSignal = !m5Break && !m1;

            if (barsSinceBreak > 3 && noFreshSignal)
            {
                int p = session == FxSession.NewYork ? LateBreakSoftPenalty_NewYork : LateBreakSoftPenalty_London;
                score -= p;
                reasons.Add($"LATE_BREAK(-{p}) bsb={barsSinceBreak} m5Break={m5Break} m1={m1}");

                // NY extra hardening only if very late AND no struct energy
                if (session == FxSession.NewYork && barsSinceBreak > 5 && !flagStruct)
                {
                    score -= LateBreakHardExtraPenalty_NY;
                    reasons.Add($"LATE_BREAK_NY_EXTRA(-{LateBreakHardExtraPenalty_NY}) bsb={barsSinceBreak} flagStruct={flagStruct}");
                }
            }

            // ==========================================================
            // METAL LATE FILTER v1 – 3) TRANSITION MIN SCORE BOOST (ADD-ONLY)
            // ==========================================================
            // HTF transition proxy without adding new ctx fields:
            // When trend loses "push" (ADX slope down / DI converging / ATR not expanding),
            // require a bit higher MinScore so only good signals pass.
            int minScoreAdj = tuning.MinScore;

            bool transitionRisk =
                (ctx.Adx_M5 >= 28 && ctx.AdxSlope_M5 <= 0 && Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 8 && !ctx.IsAtrExpanding_M5)
                || (ctx.IsRange_M5 && !ctx.IsAtrExpanding_M5 && ctx.BarsSinceImpulse_M5 > 2);

            if (transitionRisk)
            {
                minScoreAdj += TransitionMinScoreBoost;
                reasons.Add($"TRANSITION_MIN+{TransitionMinScoreBoost}(min={tuning.MinScore}->{minScoreAdj}) adx={ctx.Adx_M5:F1} slope={ctx.AdxSlope_M5:F3} diΔ={Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5):F1} atrExp={ctx.IsAtrExpanding_M5} range={ctx.IsRange_M5}");
            }

            // ===============================
            // STRUCTURE CONFIRM (EXISTING)
            // ===============================
            var bars = ctx.M5;
            int last = bars.Count - 2;

            if (ctx.TrendDirection == TradeDirection.Long)
            {
                if (bars[last].Close <= hi)
                    return InvalidDecision(ctx, session, tag, score, minScoreAdj, "NO_FLAG_HIGH_BREAK", reasons);

                if (IsLowerHighSequence(bars, last))
                    return InvalidDecision(ctx, session, tag, score, minScoreAdj, "LOWER_HIGH_SEQUENCE", reasons);
            }

            if (ctx.TrendDirection == TradeDirection.Short)
            {
                if (bars[last].Close >= lo)
                    return InvalidDecision(ctx, session, tag, score, minScoreAdj, "NO_FLAG_LOW_BREAK", reasons);

                if (IsHigherLowSequence(bars, last))
                    return InvalidDecision(ctx, session, tag, score, minScoreAdj, "HIGHER_LOW_SEQUENCE", reasons);
            }

            // ===============================
            // BODY DOMINANCE (EXISTING)
            // ===============================
            double range = bars[last].High - bars[last].Low;
            double body = Math.Abs(bars[last].Close - bars[last].Open);

            if (range > 0)
            {
                double ratio = body / range;
                if (ratio < 0.60)
                {
                    score -= 4;
                    reasons.Add("WEAK_BODY(-4)");
                }
            }

            if (!BodyAligned(ctx))
                return InvalidDecision(ctx, session, tag, score, minScoreAdj, "BODY_MISMATCH", reasons);

            if (m1)
            {
                score += tuning.M1TriggerBonus;
                reasons.Add($"+M1({tuning.M1TriggerBonus})");
            }

            if (flagStruct)
            {
                score += tuning.FlagQualityBonus;
                reasons.Add($"+FQ({tuning.FlagQualityBonus})");
            }

            if (score < minScoreAdj)
                return InvalidDecision(ctx, session, tag, score, minScoreAdj, "LOW_SCORE", reasons);

            reasons.Add("ACCEPT");

            return ValidDecision(ctx, session, tag, score, minScoreAdj, rangeAtr, reasons);
        }

        // ===============================
        // Helpers (EXISTING + ADD-ONLY)
        // ===============================

        private static double ResolveAdxClimax(XAU_InstrumentProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.Symbol))
                return DefaultAdxClimax;

            string s = profile.Symbol.ToUpperInvariant();

            if (s.Contains("XAU"))
                return XauAdxClimax;

            if (s.Contains("XAG"))
                return XagAdxClimax;

            // placeholder for platinum etc (kept generic)
            return DefaultAdxClimax;
        }

        private static bool IsLowerHighSequence(Bars bars, int last)
        {
            int h1 = last - 1;
            int h2 = last - 2;
            int h3 = last - 3;
            if (h3 < 0) return false;

            return bars[h1].High < bars[h2].High &&
                   bars[h2].High < bars[h3].High;
        }

        private static bool IsHigherLowSequence(Bars bars, int last)
        {
            int l1 = last - 1;
            int l2 = last - 2;
            int l3 = last - 3;
            if (l3 < 0) return false;

            return bars[l1].Low > bars[l2].Low &&
                   bars[l2].Low > bars[l3].Low;
        }

        private static int ComputeStepPenalty(double diffAtr, int per01, int cap)
        {
            if (diffAtr <= 0) return 0;
            int steps = (int)Math.Ceiling(diffAtr / 0.10);
            int p = steps * per01;
            return Math.Min(p, cap);
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
            string note =
                $"[{tag}] {ctx.Symbol} {session} dir={ctx.TrendDirection} " +
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
            string note =
                $"[{tag}] {ctx?.Symbol} {session} dir={ctx?.TrendDirection} " +
                $"Score={score} Min={minScore} Decision=REJECT Reason={reason} | " +
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

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };

        private static bool IsImpulseExhaustedXau(
            EntryContext ctx,
            int lookback,
            double netAtrMult,
            double bodyAtrMult)
        {
            var bars = ctx.M5;
            if (bars.Count < lookback + 2 || ctx.AtrM5 <= 0)
                return false;

            int end = bars.Count - 2;
            int start = end - lookback;

            double netMove = Math.Abs(bars[end].Close - bars[start].Close);
            double body = Math.Abs(bars[end].Close - bars[end].Open);

            return netMove > ctx.AtrM5 * netAtrMult &&
                   body > ctx.AtrM5 * bodyAtrMult;
        }
    }
}
