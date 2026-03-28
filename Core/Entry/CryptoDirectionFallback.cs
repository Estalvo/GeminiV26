using System;
using GeminiV26.Core;

namespace GeminiV26.Core.Entry
{
    internal static class CryptoDirectionFallback
    {
        private static readonly int MinimumFallbackConfidence = (int)Math.Ceiling(EntryDecisionPolicy.MinScoreThreshold * 0.6);

        public static bool ApplyIfEligible(EntryContext ctx, EntryEvaluation eval, string reason)
        {
            if (ctx == null || eval == null)
                return false;

            TradeDirection biasDirection = ctx.LogicBiasDirection;
            if (eval.Direction != TradeDirection.None || biasDirection == TradeDirection.None)
                return false;

            bool patternDetected = DetectPattern(ctx, biasDirection);

            eval.Direction = biasDirection;
            eval.RawDirection = biasDirection;
            eval.FallbackDirectionUsed = true;
            eval.PatternDetected = patternDetected;
            int sourceConfidence = ctx.LogicBiasConfidence > 0 ? ctx.LogicBiasConfidence : MinimumFallbackConfidence;
            eval.RawLogicConfidence = Math.Max(MinimumFallbackConfidence, Math.Min(100, sourceConfidence));
            eval.LogicConfidence = eval.RawLogicConfidence;

            if (eval.Score <= 0)
                eval.Score = 40;

            eval.Reason = string.IsNullOrWhiteSpace(eval.Reason)
                ? "FALLBACK_DIRECTION_FROM_LOGIC_BIAS"
                : $"{eval.Reason}|FALLBACK_DIRECTION_FROM_LOGIC_BIAS";

            return true;
        }

        public static bool DetectPattern(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null || direction == TradeDirection.None)
                return false;

            bool impulseDetected =
                (ctx.HasImpulse_M1 && (ctx.ImpulseDirection == TradeDirection.None || ctx.ImpulseDirection == direction)) ||
                (ctx.HasImpulse_M5 && (ctx.ImpulseDirection == TradeDirection.None || ctx.ImpulseDirection == direction));

            bool breakoutDetected =
                (ctx.HasBreakout_M1 && (ctx.BreakoutDirection == TradeDirection.None || ctx.BreakoutDirection == direction)) ||
                ctx.RangeBreakDirection == direction ||
                (direction == TradeDirection.Long
                    ? (ctx.FlagBreakoutUp || ctx.FlagBreakoutUpConfirmed)
                    : (ctx.FlagBreakoutDown || ctx.FlagBreakoutDownConfirmed));

            bool pullbackValid =
                ctx.PullbackBars_M5 >= 2 &&
                (ctx.IsPullbackDecelerating_M5 || ctx.HasReactionCandle_M5 || ctx.LastClosedBarInTrendDirection);

            return impulseDetected || breakoutDetected || pullbackValid;
        }

    }
}
