using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.EntryTypes;

namespace GeminiV26.EntryTypes.METAL
{
    public sealed class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        private const int MinBars = 20;
        private const int MaxBarsSinceImpulse = 7;
        private const int MaxLateTriggerScore = 55;
        private const int StrictBreakoutPersistenceBars = 2;
        private const int RelaxedBreakoutPersistenceBars = 3;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Reject(ctx, TradeDirection.None, "SESSION_DISABLED", "[ENTRY][XAU_FLAG][BLOCK_SESSION]");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < MinBars)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY", "[ENTRY][XAU_FLAG][BLOCK_CTX]");

            var dir = ctx.LogicBiasDirection;
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_LOGIC_BIAS", "[ENTRY][XAU_FLAG][BLOCK_DIR]");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            bool recentImpulseWidened =
                !ctx.Structure.HasImpulse &&
                barsSinceImpulse >= 0 &&
                barsSinceImpulse <= 10 &&
                ctx.Structure.ImpulseStrength >= 0.50;
            bool flagShapeWidened =
                !ctx.Structure.HasFlag &&
                ctx.Structure.FlagBars >= 2 &&
                (ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal || ctx.HasReactionCandle_M5);
            if ((!ctx.Structure.HasImpulse && !recentImpulseWidened) || (!ctx.Structure.HasFlag && !flagShapeWidened))
                return Reject(ctx, dir, "NO_STRUCTURE", "[ENTRY][XAU_FLAG][WIDEN_STILL_BLOCK][CODE=NO_STRUCTURE]");
            if (recentImpulseWidened)
                ctx.Log?.Invoke($"[ENTRY][XAU_FLAG][WIDEN_ALLOW] code=RECENT_IMPULSE barsSinceImpulse={barsSinceImpulse}");
            if (flagShapeWidened)
                ctx.Log?.Invoke($"[ENTRY][XAU_FLAG][WIDEN_ALLOW] code=MESSY_FLAG flagBars={ctx.Structure.FlagBars}");

            double compressionScore = dir == TradeDirection.Long
                ? ctx.FlagCompressionScoreLong_M5
                : ctx.FlagCompressionScoreShort_M5;
            bool strictFlagRange =
                ctx.Structure.FlagCompression <= 0.72 &&
                ctx.Structure.FlagBars >= 2;
            bool widenedFlagRange =
                ctx.Structure.FlagCompression <= 0.90 &&
                compressionScore >= 0.28 &&
                ctx.Structure.ImpulseStrength >= 0.45;

            if (!strictFlagRange && !widenedFlagRange)
                return Reject(ctx, dir, "NO_FLAG_RANGE", "[ENTRY][XAU_FLAG][BLOCK] reason=NO_FLAG_RANGE");

            ctx.Log?.Invoke(
                strictFlagRange
                    ? $"[ENTRY][XAU_FLAG][RECOGNIZED] reason=FLAG_RANGE_STRICT compression={ctx.Structure.FlagCompression:0.00} bars={ctx.Structure.FlagBars}"
                    : $"[ENTRY][XAU_FLAG][RECOGNIZED] reason=FLAG_RANGE_WIDENED compression={ctx.Structure.FlagCompression:0.00} score={compressionScore:0.00} impulse={ctx.Structure.ImpulseStrength:0.00}");

            bool breakoutConfirmed = dir == TradeDirection.Long
                ? (ctx.FlagBreakoutUpConfirmed || ctx.Structure.FlagBreakoutUp)
                : (ctx.FlagBreakoutDownConfirmed || ctx.Structure.FlagBreakoutDown);

            bool breakoutPersistent = dir == TradeDirection.Long
                ? ctx.BreakoutUpBarsSince <= StrictBreakoutPersistenceBars
                : ctx.BreakoutDownBarsSince <= StrictBreakoutPersistenceBars;
            bool breakoutPersistentWidened = dir == TradeDirection.Long
                ? ctx.BreakoutUpBarsSince <= RelaxedBreakoutPersistenceBars
                : ctx.BreakoutDownBarsSince <= RelaxedBreakoutPersistenceBars;
            bool triggerProxy = ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5 || ctx.IsAtrExpanding_M5;

            bool highQualityLocalBreak =
                breakoutConfirmed &&
                (breakoutPersistent || (breakoutPersistentWidened && triggerProxy)) &&
                ctx.LastClosedBarInTrendDirection &&
                (ctx.IsAtrExpanding_M5 || ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5);

            if (breakoutConfirmed && !breakoutPersistent && breakoutPersistentWidened && triggerProxy)
                ctx.Log?.Invoke("[ENTRY][XAU_FLAG][RECOGNIZED] reason=PERSISTENCE_WIDENED");

            double htfConf = ctx.ResolveAssetHtfConfidence01();
            var htfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                htfConf >= 0.6 &&
                htfDir != TradeDirection.None &&
                htfDir != dir;

            if (htfMismatch && !highQualityLocalBreak)
                return Reject(ctx, dir, "HTF_MISMATCH", "[ENTRY][XAU_FLAG][BLOCK_HTF]");

            double lateScore = dir == TradeDirection.Long ? ctx.TriggerLateScoreLong : ctx.TriggerLateScoreShort;
            if (barsSinceImpulse < 0 || barsSinceImpulse > MaxBarsSinceImpulse || lateScore >= MaxLateTriggerScore)
                return Reject(ctx, dir, "LATE_BLOCK", "[ENTRY][XAU_FLAG][BLOCK] reason=LATE_BLOCK");

            if (!highQualityLocalBreak)
                return Reject(ctx, dir, "NO_PERSISTENT_BREAKOUT", "[ENTRY][XAU_FLAG][BLOCK] reason=NO_PERSISTENT_BREAKOUT");

            if (!ctx.M1TriggerInTrendDirection && !ctx.HasReactionCandle_M5)
                return Reject(ctx, dir, "TRIGGER_NOT_QUALIFIED", "[ENTRY][XAU_FLAG][BLOCK] reason=TRIGGER_NOT_QUALIFIED");

            ctx.Log?.Invoke("[ENTRY][XAU_FLAG][STRUCT_OK]");
            ctx.Log?.Invoke("[ENTRY][XAU_FLAG][TRIGGER_OK]");
            ctx.Log?.Invoke("[ENTRY][XAU_FLAG][RECOGNIZED] reason=STRUCTURE_FIRST_OK");

            int score = 60;
            if (ctx.Structure.FlagCompression <= 0.35)
                score += 8;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.M1TriggerInTrendDirection)
                score += 6;
            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = "XAU_FLAG_STRUCTURE_FIRST_OK"
            };
        }

        private static EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, string reason, string log)
        {
            ctx?.Log?.Invoke(log);
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                IsValid = false,
                Score = 0,
                Reason = reason
            };
        }
    }
}
