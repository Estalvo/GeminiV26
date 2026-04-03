using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.FX;
using System;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_RangeBreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_RangeBreakout;

        private const int MinRangeBars = 20;
        private const double MinBreakATR = 0.3;
        private const int MaxFakeoutBars = 1;
        private const double MaxSlopeForRange = 0.0005;
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            FxDirectionValidation.LogDirectionDebug(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowBreakout)
            {
                return Invalid(ctx, "SESSION_MATRIX_BREAKOUT_DISABLED");
            }
            if (ctx == null || !ctx.IsReady)
            {
                return Invalid(ctx, "CTX_NOT_READY;");
            }

            if (ctx.LogicBias == TradeDirection.None)
            {
                return Invalid(ctx, "NO_LOGIC_BIAS");
            }

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MinRangeBars)
            {
                return Invalid(ctx, "NoRange;");
            }

            if (Math.Abs(ctx.Ema21Slope_M5) > MaxSlopeForRange)
            {
                return Invalid(ctx, "Trending;");
            }

            if (FxDirectionValidation.ShouldRejectLowConfidenceHtfConflict(ctx))
            {
                return Invalid(ctx, "FX_LOW_CONF_HTF_CONFLICT");
            }

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Invalid(ctx, "NO_LOGIC_BIAS");
        }

        private EntryEvaluation EvaluateSide(EntryContext ctx, TradeDirection dir)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = 0,
                IsValid = false,
                Reason = ""
            };

            int score = 20;

            if (ctx.RangeBreakDirection == dir && ctx.RangeBreakAtrSize_M5 >= MinBreakATR)
                score += 15;
            else
                score -= 10;

            if (ctx.RangeFakeoutBars_M1 <= MaxFakeoutBars)
                score += 10;
            else
                score -= 15;

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = ctx.RangeBreakDirection == dir;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = (ctx.RangeBreakDirection == dir && ctx.M1TriggerInTrendDirection) || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);

            if (ctx.IsAtrExpanding_M5)
                score += 5;

            if (ctx.RangeBreakDirection != dir && ctx.RangeBreakDirection != TradeDirection.None)
                score -= 10;

            score = TriggerScoreModel.Apply(ctx, $"FX_RANGE_BREAKOUT_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_RANGE_BREAK_TRIGGER");


            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);
            eval.Score = score;

            if (score < MIN_SCORE)
            {
                eval.IsValid = false;
                eval.Reason += "BelowMinScore;";
            }
            else
            {
                eval.IsValid = true;
            }

            return eval;
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "FX_RangeBreakoutEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = ApplyMandatoryEntryAdjustments(ctx, TradeDirection.None, 0, false),
                IsValid = false,
                Reason = reason
            };

    }
}
