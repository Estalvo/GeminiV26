using System;
using GeminiV26.Core;

namespace GeminiV26.Core.Entry
{
    internal static class CryptoDirectionFallback
    {
        public static bool ApplyIfEligible(EntryContext ctx, EntryEvaluation eval, string reason)
        {
            if (ctx == null || eval == null)
                return false;

            TradeDirection biasDirection = ctx.LogicBiasDirection;
            if (eval.Direction != TradeDirection.None || biasDirection == TradeDirection.None)
                return false;

            if (IsHardNoDirectionReason(reason))
                return false;

            bool patternDetected = DetectPattern(ctx, biasDirection);
            bool structurePresent = patternDetected || HasContinuationStructure(ctx, biasDirection);
            if (!structurePresent)
                return false;

            eval.Direction = biasDirection;
            eval.RawDirection = biasDirection;
            eval.FallbackDirectionUsed = true;
            eval.PatternDetected = patternDetected;
            eval.RawLogicConfidence = Math.Max(15, Math.Min(35, ctx.LogicBiasConfidence > 0 ? ctx.LogicBiasConfidence : 20));
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

        private static bool HasContinuationStructure(EntryContext ctx, TradeDirection direction)
        {
            bool hasFlag = direction == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5;
            bool hasRange = ctx.IsRange_M5 && ctx.RangeBarCount_M5 >= 10;
            return hasFlag || hasRange;
        }

        private static bool IsHardNoDirectionReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            string normalized = reason.ToUpperInvariant();
            return normalized.Contains("CTX_NOT_READY")
                || normalized.Contains("NO_LOGIC_BIAS")
                || normalized.Contains("HTF_MISMATCH")
                || normalized.Contains("DISABLED")
                || normalized.Contains("NO_RANGE")
                || normalized.Contains("NO_FLAG_WINDOW")
                || normalized.Contains("LATE_FLAG")
                || normalized.Contains("ATR_ZERO")
                || normalized.Contains("NO_RECENT_IMPULSE");
        }
    }
}
