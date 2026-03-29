using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.FX
{
    internal static class FxDirectionValidation
    {
        public static void LogDirectionDebug(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
        }

        public static bool ShouldBlockHtfMismatch(EntryContext ctx)
        {
            if (ctx == null)
                return false;

            string symbol = Normalize(ctx.Symbol);
            if (symbol == "GBPUSD")
                return LegacyShouldBlockHtfMismatch(ctx, symbol);

            var logicBias = ctx.LogicBiasDirection;
            if (logicBias == TradeDirection.None)
                return false;

            var htfDirection = ctx.FxHtfAllowedDirection;
            var htfConfidence = ctx.FxHtfConfidence01;

            if (htfDirection == TradeDirection.None || htfDirection == logicBias)
                return false;

            if (htfConfidence < 0.80 || ctx.LogicBiasConfidence >= 60)
            {
                ctx.Log?.Invoke(
                    $"[HTF][PENALTY] mismatch applied sym={symbol} logicBias={logicBias} logicConf={ctx.LogicBiasConfidence} " +
                    $"htf={htfDirection}/{htfConfidence:F2}");
                return false;
            }

            ctx.Log?.Invoke(
                $"[HTF][BLOCK] strong opposite HTF + weak LTF sym={symbol} logicBias={logicBias} " +
                $"logicConf={ctx.LogicBiasConfidence} htf={htfDirection}/{htfConfidence:F2}");
            return true;
        }

        public static bool ShouldRejectLowConfidenceHtfConflict(EntryContext ctx)
        {
            return false;
        }

        public static int GetLowConfidenceHtfConflictPenalty(EntryContext ctx)
        {
            if (ctx == null)
                return 0;

            var logicBias = ctx.LogicBiasDirection;
            if (logicBias == TradeDirection.None)
                return 0;

            var htfDirection = ctx.FxHtfAllowedDirection;
            if (htfDirection == TradeDirection.None || htfDirection == logicBias)
                return 0;

            if (ctx.LogicBiasConfidence >= 60)
                return 0;

            double htfConfidence = ctx.FxHtfConfidence01;
            int penalty = 6;
            if (htfConfidence >= 0.80)
                penalty += 4;
            else if (htfConfidence >= 0.60)
                penalty += 2;

            if (ctx.LogicBiasConfidence < 45)
                penalty += 2;

            penalty = Math.Max(4, Math.Min(14, penalty));
            ctx.Log?.Invoke(
                $"[HTF][LOW_CONF_MISMATCH_SOFT] sym={Normalize(ctx.Symbol)} logicBias={logicBias} logicConf={ctx.LogicBiasConfidence} " +
                $"htf={htfDirection}/{htfConfidence:F2} penalty={penalty}");
            return penalty;
        }

        public static void ApplyLowConfidenceHtfConflictSoftPenalty(EntryContext ctx, EntryEvaluation eval)
        {
            if (eval == null)
                return;

            int penalty = GetLowConfidenceHtfConflictPenalty(ctx);
            if (penalty <= 0)
                return;

            int before = eval.Score;
            eval.Score = Math.Max(0, eval.Score - penalty);
            eval.AfterPenaltyScore = eval.Score;
            eval.Reason = string.IsNullOrWhiteSpace(eval.Reason)
                ? $"HTF_SOFT_PENALTY_{penalty}"
                : $"{eval.Reason};HTF_SOFT_PENALTY_{penalty}";

            ctx?.Log?.Invoke(
                $"[HTF][SCORE_PENALTY] sym={Normalize(ctx?.Symbol)} entry={eval.Type} dir={eval.Direction} score={before}->{eval.Score} penalty={penalty}");
        }

        private static bool LegacyShouldBlockHtfMismatch(EntryContext ctx, string symbol)
        {
            var logicBias = ctx.LogicBiasDirection;
            if (logicBias == TradeDirection.None)
                return false;

            var htfDirection = ctx.FxHtfAllowedDirection;
            var htfConfidence = ctx.FxHtfConfidence01;

            if (htfDirection == TradeDirection.None || htfDirection == logicBias)
                return false;

            if (htfConfidence < 0.80 || ctx.LogicBiasConfidence >= 60)
            {
                ctx.Log?.Invoke(
                    $"[HTF][PENALTY] mismatch applied sym={symbol} logicBias={logicBias} logicConf={ctx.LogicBiasConfidence} " +
                    $"htf={htfDirection}/{htfConfidence:F2}");
                return false;
            }

            ctx.Log?.Invoke(
                $"[HTF][BLOCK] strong opposite HTF + weak LTF sym={symbol} logicBias={logicBias} " +
                $"logicConf={ctx.LogicBiasConfidence} htf={htfDirection}/{htfConfidence:F2}");
            return true;
        }

        private static bool HasDirectionalStructure(EntryContext ctx, TradeDirection direction)
        {
            if (direction == TradeDirection.Long)
            {
                return ctx.HasImpulseLong_M5 ||
                       ctx.HasPullbackLong_M5 ||
                       ctx.HasFlagLong_M5 ||
                       ctx.FlagBreakoutUp ||
                       ctx.FlagBreakoutUpConfirmed ||
                       ctx.BrokeLastSwingHigh_M5 ||
                       ctx.BreakoutDirection == TradeDirection.Long ||
                       ctx.RangeBreakDirection == TradeDirection.Long;
            }

            if (direction == TradeDirection.Short)
            {
                return ctx.HasImpulseShort_M5 ||
                       ctx.HasPullbackShort_M5 ||
                       ctx.HasFlagShort_M5 ||
                       ctx.FlagBreakoutDown ||
                       ctx.FlagBreakoutDownConfirmed ||
                       ctx.BrokeLastSwingLow_M5 ||
                       ctx.BreakoutDirection == TradeDirection.Short ||
                       ctx.RangeBreakDirection == TradeDirection.Short;
            }

            return false;
        }

        private static bool IsTrendAligned(EntryContext ctx, TradeDirection direction)
        {
            int lastClosed = ctx?.M5 == null || ctx.M5.Count < 2
                ? -1
                : ctx.M5.Count - 2;

            double lastClose = lastClosed >= 0 ? ctx.M5.ClosePrices[lastClosed] : 0.0;

            if (direction == TradeDirection.Long)
            {
                return ctx.Ema50_M5 >= ctx.Ema200_M5 &&
                       ctx.Ema21Slope_M5 >= 0 &&
                       ctx.Ema21Slope_M15 >= -0.00001 &&
                       (lastClosed < 0 || lastClose >= ctx.Ema21_M5);
            }

            if (direction == TradeDirection.Short)
            {
                return ctx.Ema50_M5 <= ctx.Ema200_M5 &&
                       ctx.Ema21Slope_M5 <= 0 &&
                       ctx.Ema21Slope_M15 <= 0.00001 &&
                       (lastClosed < 0 || lastClose <= ctx.Ema21_M5);
            }

            return false;
        }

        private static bool IsTargetedFx(string symbol)
        {
            return symbol == "AUDNZD" ||
                   symbol == "USDJPY" ||
                   symbol == "USDCHF" ||
                   symbol == "USDCAD" ||
                   symbol == "NZDUSD" ||
                   symbol == "EURJPY";
        }

        private static string Normalize(string symbol)
        {
            return string.IsNullOrWhiteSpace(symbol)
                ? string.Empty
                : symbol.Trim().ToUpperInvariant();
        }
    }
}
