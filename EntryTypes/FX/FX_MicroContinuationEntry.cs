using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Instruments.FX;
using FxTuning = GeminiV26.Instruments.FX.FxFlagSessionTuning;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_MicroContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_MicroContinuation;

        private const double MinPullbackAtr = 0.30;
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

            if (EntryDecisionPolicy.IsHardInvalid(longEval) && EntryDecisionPolicy.IsHardInvalid(shortEval))
            {
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, TradeDirection.None);
                return Invalid(ctx, "NO_VALID_SIDE");
            }

            var selected = EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval);
            EntryDirectionQuality.LogDecision(ctx, Type.ToString(), longEval, shortEval, selected.Direction);
            return EntryDecisionPolicy.Normalize(selected);
        }

        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            FxTuning tuning)
        {
            int minScore = EntryDecisionPolicy.MinScoreThreshold;
            int setupScore = 0;

            double pullbackDepthR =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 : ctx.PullbackDepthRShort_M5;

            bool trendOk =
                dir == TradeDirection.Long
                    ? (ctx.Ema21Slope_M5 > MinSlope && ctx.Ema21Slope_M15 > 0)
                    : (ctx.Ema21Slope_M5 < -MinSlope && ctx.Ema21Slope_M15 < 0);

            if (!trendOk)
                return Invalid(ctx, dir, "NO_TREND_SLOPE", 0);

            if (ctx.IsRange_M5)
                return Invalid(ctx, dir, "IN_RANGE", 0);

            if (pullbackDepthR < MinPullbackAtr)
                return Invalid(ctx, dir, "PB_TOO_SHALLOW", 50);

            if (pullbackDepthR > tuning.MaxPullbackAtr * 0.45)
                return Invalid(ctx, dir, "PB_TOO_DEEP", 49);

            if (pullbackDepthR < 0.35)
                return Invalid(ctx, dir, "PB_NOT_MATURE", 51);

            // ✅ FIX: side-aware M1
            bool m1Aligned = ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir;

            bool continuationSignal =
                (ctx.LastClosedBarInTrendDirection && ctx.HasReactionCandle_M5) ||
                m1Aligned;

            bool hasStructure =
                pullbackDepthR >= MinPullbackAtr;

            if (!hasStructure)
                setupScore -= 35;
            else
                setupScore += 15;

            bool hasContinuation =
                continuationSignal;

            if (hasContinuation)
                setupScore += 20;

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

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = m1Aligned || ctx.RangeBreakDirection == dir;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = continuationSignal;

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score = TriggerScoreModel.Apply(ctx, $"FX_MICRO_CONT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_CONTINUATION_SIGNAL");
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, minScore - 10);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
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

        private EntryEvaluation Invalid(EntryContext ctx, TradeDirection dir, string reason, int score)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = reason
            };

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "FX_MicroContinuationEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
