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

            if (!ctx.Structure.HasImpulse || !ctx.Structure.HasFlag)
                return Reject(ctx, dir, "NO_STRUCTURE", "[ENTRY][XAU_FLAG][BLOCK_STRUCTURE]");

            bool breakoutConfirmed = dir == TradeDirection.Long
                ? (ctx.FlagBreakoutUpConfirmed || ctx.Structure.FlagBreakoutUp)
                : (ctx.FlagBreakoutDownConfirmed || ctx.Structure.FlagBreakoutDown);

            bool breakoutPersistent = dir == TradeDirection.Long
                ? ctx.BreakoutUpBarsSince <= 2
                : ctx.BreakoutDownBarsSince <= 2;

            bool highQualityLocalBreak =
                breakoutConfirmed &&
                breakoutPersistent &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.IsAtrExpanding_M5;

            double htfConf = ctx.ResolveAssetHtfConfidence01();
            var htfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                htfConf >= 0.6 &&
                htfDir != TradeDirection.None &&
                htfDir != dir;

            if (htfMismatch && !highQualityLocalBreak)
                return Reject(ctx, dir, "HTF_MISMATCH", "[ENTRY][XAU_FLAG][BLOCK_HTF]");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            double lateScore = dir == TradeDirection.Long ? ctx.TriggerLateScoreLong : ctx.TriggerLateScoreShort;
            if (barsSinceImpulse < 0 || barsSinceImpulse > MaxBarsSinceImpulse || lateScore >= MaxLateTriggerScore)
                return Reject(ctx, dir, "STALE_OR_EXPIRED", "[ENTRY][XAU_FLAG][INVALID_STALE]");

            if (!highQualityLocalBreak)
                return Reject(ctx, dir, "NO_PERSISTENT_BREAKOUT", "[ENTRY][XAU_FLAG][BLOCK_BREAK_QUALITY]");

            if (!ctx.M1TriggerInTrendDirection && !ctx.HasReactionCandle_M5)
                return Reject(ctx, dir, "TRIGGER_NOT_QUALIFIED", "[ENTRY][XAU_FLAG][BLOCK_TRIGGER]");

            ctx.Log?.Invoke("[ENTRY][XAU_FLAG][STRUCT_OK]");
            ctx.Log?.Invoke("[ENTRY][XAU_FLAG][TRIGGER_OK]");

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
