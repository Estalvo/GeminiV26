using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.EntryTypes;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public sealed class Index_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Flag;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Reject(ctx, "SESSION_MATRIX_ALLOWFLAG_DISABLED", 0, TradeDirection.None);

            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, "CTX_NOT_READY", 0, TradeDirection.None);

            if (!ctx.Structure.HasImpulse)
            {
                ctx.Log?.Invoke("[ENTRY][FLAG][STRUCTURE_ERROR] violation=no_trade_without_impulse");
                return Reject(ctx, "no_impulse", 0, TradeDirection.None);
            }

            if (!ctx.Structure.HasPullback)
            {
                ctx.Log?.Invoke("[ENTRY][FLAG][STRUCTURE_ERROR] violation=no_flag_without_pullback_retrace");
                return Reject(ctx, "no_pullback", 0, TradeDirection.None);
            }

            if (!ctx.Structure.HasFlag)
            {
                ctx.Log?.Invoke("[ENTRY][FLAG][STRUCTURE_ERROR] violation=no_flag_entry_without_compression");
                return Reject(ctx, "no_flag", 0, TradeDirection.None);
            }

            if (ctx.Structure.StructureDirection == TradeDirection.None)
            {
                ctx.Log?.Invoke("[ENTRY][FLAG][STRUCTURE_ERROR] violation=direction_must_come_from_impulse");
                return Reject(ctx, "structure_direction_missing", 0, TradeDirection.None);
            }

            var direction = ctx.Structure.StructureDirection;
            int score = (int)Math.Round(100.0 * (
                0.4 * ctx.Structure.ImpulseStrength +
                0.3 * (1.0 - ctx.Structure.PullbackDepth) +
                0.3 * (1.0 - ctx.Structure.FlagCompression)));
            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            ctx.Log?.Invoke("[ENTRY][FLAG][STRUCTURE_OK]");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = score,
                IsValid = true,
                Reason = "STRUCTURE_IMPULSE_PULLBACK_FLAG_OK"
            };
        }

        private static EntryEvaluation Reject(EntryContext ctx, string reason, int score, TradeDirection dir)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Flag,
                Direction = dir,
                IsValid = false,
                Score = Math.Max(0, score),
                Reason = reason
            };
        }
    }
}
