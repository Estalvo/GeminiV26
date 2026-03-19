using System;
using System.Collections.Generic;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    internal static class XauEntryDecisionPolicy
    {
        public static bool HasDataIntegrityIssue(EntryContext ctx)
        {
            if (ctx == null)
                return true;

            return double.IsNaN(ctx.AtrM5)
                   || double.IsInfinity(ctx.AtrM5)
                   || double.IsNaN(ctx.Adx_M5)
                   || double.IsInfinity(ctx.Adx_M5)
                   || double.IsNaN(ctx.Ema21Slope_M5)
                   || double.IsInfinity(ctx.Ema21Slope_M5)
                   || double.IsNaN(ctx.Ema21Slope_M15)
                   || double.IsInfinity(ctx.Ema21Slope_M15);
        }

        public static bool IsTrendSafetyBlock(EntryContext ctx, TradeDirection dir, out string reason)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
            {
                reason = "CTX_NOT_READY";
                return true;
            }

            if (HasDataIntegrityIssue(ctx))
            {
                reason = "DATA_INTEGRITY";
                return true;
            }

            if (ctx.MarketState?.IsTrend != true)
            {
                reason = "NO_TREND_CONTEXT";
                return true;
            }

            if (ctx.MarketState?.IsRange == true)
            {
                reason = "WRONG_REGIME_RANGE";
                return true;
            }

            if (IsExplicitHtfHardBlock(ctx, dir))
            {
                reason = "HTF_HARD_BLOCK";
                return true;
            }

            reason = null;
            return false;
        }

        public static bool IsReversalSafetyBlock(EntryContext ctx, TradeDirection dir, out string reason)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
            {
                reason = "CTX_NOT_READY";
                return true;
            }

            if (HasDataIntegrityIssue(ctx))
            {
                reason = "DATA_INTEGRITY";
                return true;
            }

            if (!ctx.IsRange_M5)
            {
                reason = "WRONG_REGIME_NOT_RANGE";
                return true;
            }

            if (ctx.Adx_M5 > 22)
            {
                reason = $"WRONG_REGIME_TRENDING_ADX({ctx.Adx_M5:F1})";
                return true;
            }

            if (IsExplicitHtfHardBlock(ctx, dir))
            {
                reason = "HTF_HARD_BLOCK";
                return true;
            }

            reason = null;
            return false;
        }

        public static void ApplyLogicBiasScore(EntryContext ctx, TradeDirection dir, ref int score, List<string> reasons)
        {
            if (ctx == null || dir == TradeDirection.None)
                return;

            if (ctx.LogicBiasDirection == TradeDirection.None || ctx.LogicBiasConfidence <= 0)
                return;

            int delta = ResolveBiasDelta(ctx.LogicBiasConfidence);
            if (ctx.LogicBiasDirection == dir)
            {
                score += delta;
                reasons?.Add($"LOGIC_BIAS_ALIGN(+{delta})");
            }
            else
            {
                score -= delta;
                reasons?.Add($"LOGIC_BIAS_AGAINST(-{delta})");
            }
        }

        public static bool IsExplicitHtfHardBlock(EntryContext ctx, TradeDirection dir)
        {
            if (ctx == null || dir == TradeDirection.None)
                return false;

            string reason = ctx.MetalHtfReason ?? string.Empty;
            if (reason.IndexOf("HARD_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("STRONG_BLOCK", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ctx.MetalHtfAllowedDirection == TradeDirection.None || ctx.MetalHtfAllowedDirection != dir;
            }

            return false;
        }

        private static int ResolveBiasDelta(int logicBiasConfidence)
        {
            if (logicBiasConfidence >= 75)
                return 9;
            if (logicBiasConfidence >= 50)
                return 7;
            return 5;
        }
    }
}
