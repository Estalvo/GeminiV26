using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    /// <summary>
    /// FX Micro Continuation Entry
    /// Phase 3.8
    ///
    /// Cél:
    /// - Erős trendben sekély pullback utáni folytatás
    /// - NEM impulse chase
    /// - NEM klasszikus flag
    /// - Trend grind / EMA-slide piacokra
    /// </summary>
    public class FX_MicroContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_MicroContinuation;

        // =========================
        // TUNING
        // =========================
        private const int MinScore = 55;

        private const double MinPullbackAtr = 0.05;   // micro PB
        private const double MaxPullbackAtr = 0.25;   // ne legyen rendes flag

        private const double MinSlope = 0.00010;      // FX-safe

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            // =================================================
            // TREND IRÁNY
            // =================================================
            TradeDirection bias = TradeDirection.None;

            if (ctx.Ema21Slope_M5 > MinSlope && ctx.Ema21Slope_M15 > 0)
                bias = TradeDirection.Long;
            else if (ctx.Ema21Slope_M5 < -MinSlope && ctx.Ema21Slope_M15 < 0)
                bias = TradeDirection.Short;
            else
                return Invalid(ctx, "NO_TREND_SLOPE");

            // =================================================
            // TREND KÖRNYEZET (ne chop)
            // =================================================
            if (ctx.IsRange_M5)
                return Invalid(ctx, "IN_RANGE");

            // =================================================
            // MICRO PULLBACK
            // =================================================
            if (ctx.PullbackDepthAtr_M5 < MinPullbackAtr)
                return Invalid(ctx, "PB_TOO_SHALLOW");

            if (ctx.PullbackDepthAtr_M5 > MaxPullbackAtr)
                return Invalid(ctx, "PB_TOO_DEEP");

            int score = 40;

            // =================================================
            // EMA VISZONY
            // =================================================
            if (!ctx.PullbackTouchedEma21_M5)
                score -= 6;   // micro grindnál gyakori EMA-slide

            // =================================================
            // REAKCIÓ (kulcs!)
            // =================================================
            if (!ctx.LastClosedBarInTrendDirection && !ctx.HasReactionCandle_M5)
                score -= 8;   // ha egyik sincs, gyenge

            // =================================================
            // SCORE
            // =================================================
            
            if (ctx.M1TriggerInTrendDirection)
                score += 15;

            if (ctx.IsPullbackDecelerating_M5)
                score += 10;

            if (ctx.HasReactionCandle_M5)
                score += 10;

            if (ctx.IsAtrExpanding_M5)
                score -= 5;

            if (ctx.Session == FxSession.NewYork)
                score += 2;

            TradeDirection dir =
                score >= MinScore ? bias : TradeDirection.None;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = dir != TradeDirection.None,
                Reason =
                    $"FX_MICRO_CONT score={score} " +
                    $"pbATR={ctx.PullbackDepthAtr_M5:F2} " +
                    $"m1={ctx.M1TriggerInTrendDirection}"
            };
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
    }
}
