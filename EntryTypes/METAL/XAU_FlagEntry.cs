using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        // =====================================================
        // Shared tunables (XAU)
        // =====================================================
        private const int BaseScore = 80;

        // London (build-up)
        private const int MaxBarsSinceImpulse_London = 6;
        private const int FlagBars_London = 3;
        private const double MaxFlagRangeAtr_London = 0.75;
        private const double MaxDistFromEmaAtr_London = 1.05;
        private const double BreakBufferAtr_London = 0.08;

        // New York (spike / continuation)
        private const int MaxBarsSinceImpulse_NY = 3;
        private const int FlagBars_NY = 2;
        private const double MaxFlagRangeAtr_NY = 0.65;
        private const double MaxDistFromEmaAtr_NY = 0.90;
        private const double BreakBufferAtr_NY = 0.10;

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

            // ✅ XAU FIX: ha nincs session kitöltve (default enum value = 0),
            // kezeljük Londonként
            if ((int)session == 0)
                session = FxSession.London;

            return session switch
            {
                FxSession.London => EvaluateLondon(ctx),
                FxSession.NewYork => EvaluateNewYork(ctx),
                _ => Invalid(ctx, "NO_SESSION")
            };
        }

        // =====================================================
        // LONDON – STRUCTURAL / BUILD-UP FLAG
        // =====================================================
        private EntryEvaluation EvaluateLondon(EntryContext ctx)
        {
            int score = BaseScore;

            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NO_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > MaxBarsSinceImpulse_London)
                return Invalid(ctx, "STALE_IMPULSE");

            // Londonban ATR expanding gyanús, de nem hard tilt
            if (ctx.IsAtrExpanding_M5)
                score -= 8;

            if (!TryComputeFlag(ctx, FlagBars_London, out var hi, out var lo, out var rangeAtr))
                return Invalid(ctx, "FLAG_FAIL");

            if (rangeAtr > MaxFlagRangeAtr_London)
                return Invalid(ctx, "FLAG_TOO_WIDE");

            if (IsOverextended(ctx, MaxDistFromEmaAtr_London))
                return Invalid(ctx, "OVEREXTENDED");

            if (!BreakoutClose(ctx, hi, lo, BreakBufferAtr_London))
                return Invalid(ctx, "NO_BREAKOUT_CLOSE");

            // Londonban body alignment kötelező (anti fake)
            if (!BodyAligned(ctx))
                return Invalid(ctx, "BODY_MISMATCH");

            if (ctx.M1TriggerInTrendDirection)
                score += 5;

            if (ctx.IsValidFlagStructure_M5)
                score += 3;

            if (score < 65)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return Valid(ctx, score, rangeAtr, "XAU_FLAG_LONDON");
        }

        // =====================================================
        // NEW YORK – SPIKE / CONTINUATION
        // =====================================================
        private EntryEvaluation EvaluateNewYork(EntryContext ctx)
        {
            int score = BaseScore;

            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NO_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > MaxBarsSinceImpulse_NY)
                return Invalid(ctx, "STALE_IMPULSE");

            // NY-ban M1 trigger kötelező
            if (!ctx.M1TriggerInTrendDirection)
                return Invalid(ctx, "NO_M1_TRIGGER");

            // ATR expanding itt ELVÁRT (nem tiltás!)
            if (!ctx.IsAtrExpanding_M5)
                score -= 5;

            if (!TryComputeFlag(ctx, FlagBars_NY, out var hi, out var lo, out var rangeAtr))
                return Invalid(ctx, "FLAG_FAIL");

            if (rangeAtr > MaxFlagRangeAtr_NY)
                return Invalid(ctx, "FLAG_TOO_WIDE");

            if (IsOverextended(ctx, MaxDistFromEmaAtr_NY))
                return Invalid(ctx, "OVEREXTENDED");

            if (!BreakoutClose(ctx, hi, lo, BreakBufferAtr_NY))
                return Invalid(ctx, "NO_BREAKOUT_CLOSE");

            // NY-ban is kötelező a body alignment (chase védelem)
            if (!BodyAligned(ctx))
                return Invalid(ctx, "BODY_MISMATCH");

            if (ctx.IsValidFlagStructure_M5)
                score += 3;

            if (score < 60)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return Valid(ctx, score, rangeAtr, "XAU_FLAG_NY");
        }

        // =====================================================
        // HELPERS
        // =====================================================
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
            int flagEnd = lastClosed - 1;
            int flagStart = flagEnd - flagBars + 1;
            if (flagStart < 2)
                return false;

            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            if (ctx.AtrM5 <= 0)
                return false;

            double range = hi - lo;
            rangeAtr = range / ctx.AtrM5;
            return true;
        }

        private static bool IsOverextended(EntryContext ctx, double maxDistFromEmaAtr)
        {
            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            double close = bars[lastClosed].Close;
            double dist = Math.Abs(close - ctx.Ema21_M5);
            return dist > ctx.AtrM5 * maxDistFromEmaAtr;
        }

        private static bool BreakoutClose(EntryContext ctx, double hi, double lo, double bufAtr)
        {
            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            double close = bars[lastClosed].Close;
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
            int lastClosed = bars.Count - 2;
            double open = bars[lastClosed].Open;
            double close = bars[lastClosed].Close;

            if (ctx.TrendDirection == TradeDirection.Long)
                return close > open;

            if (ctx.TrendDirection == TradeDirection.Short)
                return close < open;

            return false;
        }

        private EntryEvaluation Valid(EntryContext ctx, int score, double rangeAtr, string tag)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = ctx.TrendDirection,
                Score = score,
                IsValid = true,
                Reason = $"{tag} dir={ctx.TrendDirection} score={score} rangeATR={rangeAtr:F2}"
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
