using GeminiV26.Core.Entry;
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
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowBreakout)
            {
                return new EntryEvaluation
                {
                    Symbol = ctx?.Symbol,
                    Type = Type,
                    IsValid = false,
                    Reason = "SESSION_MATRIX_BREAKOUT_DISABLED"
                };
            }
            if (ctx == null || !ctx.IsReady)
            {
                return new EntryEvaluation
                {
                    Symbol = ctx?.Symbol,
                    Type = Type,
                    IsValid = false,
                    Reason = "CTX_NOT_READY;"
                };
            }

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                IsValid = false,
                Reason = ""
            };

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MinRangeBars)
            {
                eval.Reason += "NoRange;";
                return eval;
            }

            if (Math.Abs(ctx.Ema21Slope_M5) > MaxSlopeForRange)
            {
                eval.Reason += "Trending;";
                return eval;
            }

            if (ctx.RangeBreakDirection == TradeDirection.None)
            {
                eval.Reason += "NoBreak;";
                return eval;
            }

            eval.Direction = ctx.RangeBreakDirection;
            int score = 20;

            if (ctx.RangeBreakAtrSize_M5 >= MinBreakATR)
                score += 15;
            else
                score -= 10;

            if (ctx.RangeFakeoutBars_M1 <= MaxFakeoutBars)
                score += 10;
            else
                score -= 15;

            bool breakoutDetected = ctx.RangeBreakDirection == eval.Direction;
            bool strongCandle = ctx.LastClosedBarInTrendDirection;
            bool followThrough = ctx.M1TriggerInTrendDirection || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == eval.Direction);

            if (ctx.IsAtrExpanding_M5)
                score += 5;

            score = TriggerScoreModel.Apply(ctx, $"FX_RANGE_BREAKOUT_{eval.Direction}", score, breakoutDetected, strongCandle, followThrough, "NO_RANGE_BREAK_TRIGGER");
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
    }
}
