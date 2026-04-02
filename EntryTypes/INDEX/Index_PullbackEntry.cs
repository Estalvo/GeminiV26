using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.EntryTypes;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Pullback;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Reject(ctx, TradeDirection.None, 0, "SESSION_MATRIX_ALLOWPULLBACK_DISABLED");

            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, TradeDirection.None, 0, "CTX_NOT_READY");

            if (!ctx.Structure.HasImpulse)
            {
                ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_ERROR] violation=no_trade_without_impulse");
                return Reject(ctx, TradeDirection.None, 0, "no_impulse");
            }

            if (!ctx.Structure.HasPullback)
            {
                ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_ERROR] violation=no_pullback_entry_without_retrace");
                return Reject(ctx, TradeDirection.None, 0, "no_pullback");
            }

            bool useEarly = ctx.Structure.PullbackEarlySignal;
            bool useConfirmed = ctx.Structure.PullbackConfirmedSignal;

            if (!useEarly && !useConfirmed)
            {
                ctx.Log?.Invoke("[ENTRY][PULLBACK][WAIT_SIGNAL]");
                return Reject(ctx, TradeDirection.None, 0, "no_signal");
            }

            if (ctx.Structure.StructureDirection == TradeDirection.None)
            {
                ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_ERROR] violation=direction_must_come_from_impulse");
                return Reject(ctx, TradeDirection.None, 0, "structure_direction_missing");
            }

            bool isConfirmedEntry = useConfirmed;

            var direction = ctx.Structure.StructureDirection;
            double timingScore = isConfirmedEntry ? 1.0 : 0.7;
            int score = (int)Math.Round(100.0 * (
                0.4 * ctx.Structure.ImpulseStrength +
                0.4 * (1.0 - ctx.Structure.PullbackDepth) +
                0.2 * timingScore));
            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_OK]");
            ctx.Log?.Invoke($"[ENTRY][PULLBACK][SIGNAL] type={(isConfirmedEntry ? "CONFIRMED" : "EARLY")}");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = score,
                IsValid = true,
                Reason = "STRUCTURE_IMPULSE_PULLBACK_OK"
            };
        }

        private static EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, int score, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Pullback,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = reason
            };
        }
    }
}
