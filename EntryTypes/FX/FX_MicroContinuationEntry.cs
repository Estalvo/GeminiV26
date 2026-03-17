using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;
using FxTuning = GeminiV26.Instruments.FX.FxFlagSessionTuning;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_MicroContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_MicroContinuation;

        private const double MinPullbackAtr = 0.05;
        private const double MinSlope = 0.00010;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);

            if (fx == null)
                return Invalid(ctx, "NO_FX_PROFILE");

            if (!fx.FlagTuning.TryGetValue(ctx.Session, out var tuning))
                return Invalid(ctx, "NO_SESSION_TUNING");

            var longEval = EvaluateSide(TradeDirection.Long, ctx, tuning);
            var shortEval = EvaluateSide(TradeDirection.Short, ctx, tuning);

            if (longEval.IsValid && !shortEval.IsValid)
                return longEval;

            if (!longEval.IsValid && shortEval.IsValid)
                return shortEval;

            if (longEval.IsValid && shortEval.IsValid)
                return longEval.Score >= shortEval.Score ? longEval : shortEval;

            return Invalid(ctx, "NO_VALID_SIDE");
        }

        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            FxTuning tuning)
        {
            int minScore = Math.Max(40, tuning.MinScore - 5);

            double pullbackDepthR =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 : ctx.PullbackDepthRShort_M5;

            bool trendOk =
                dir == TradeDirection.Long
                    ? (ctx.Ema21Slope_M5 > MinSlope && ctx.Ema21Slope_M15 > 0)
                    : (ctx.Ema21Slope_M5 < -MinSlope && ctx.Ema21Slope_M15 < 0);

            if (!trendOk)
                return Invalid(ctx, "NO_TREND_SLOPE");

            if (ctx.IsRange_M5)
                return Invalid(ctx, "IN_RANGE");

            if (pullbackDepthR < MinPullbackAtr)
                return Invalid(ctx, "PB_TOO_SHALLOW");

            if (pullbackDepthR > tuning.MaxPullbackAtr * 0.45)
                return Invalid(ctx, "PB_TOO_DEEP");

            // ✅ FIX: side-aware M1
            bool m1Aligned = ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir;

            bool continuationSignal =
                ctx.LastClosedBarInTrendDirection ||
                ctx.HasReactionCandle_M5 ||
                m1Aligned;

            if (!continuationSignal)
                return Invalid(ctx, "NO_CONTINUATION_SIGNAL");

            int score = 50;

            if (!ctx.PullbackTouchedEma21_M5)
                score -= 6;

            if (!ctx.LastClosedBarInTrendDirection && !ctx.HasReactionCandle_M5)
                score -= 6;

            // ✅ FIX: side-aware scoring
            if (m1Aligned)
                score += 15;

            if (ctx.IsPullbackDecelerating_M5)
                score += 10;

            if (ctx.HasReactionCandle_M5)
                score += 10;

            if (ctx.IsAtrExpanding_M5)
                score += 3;

            if (ctx.Session == FxSession.NewYork)
                score += 2;

            TradeDirection finalDir =
                score >= minScore ? dir : TradeDirection.None;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = finalDir,
                Score = score,
                IsValid = finalDir != TradeDirection.None,
                Reason =
                    $"FX_MICRO_CONT score={score} " +
                    $"pbR={pullbackDepthR:F2} " +
                    $"m1={m1Aligned}"
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