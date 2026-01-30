using GeminiV26.Core.Entry;
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
        private const int MIN_SCORE = 50;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = "FX_RESET_DISABLED_RANGE_BREAKOUT"
            };
        }
        
        /*
        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            // 🛑 DATA-ONLY PIPELINE GUARD
            if (!ctx.IsReady)
            {
                return new EntryEvaluation
                {
                    Symbol = ctx.Symbol,
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

            if (ctx.M1TriggerInTrendDirection)
                score += 10;

            if (ctx.IsAtrExpanding_M5)
                score += 5;

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
        */
    }
}
