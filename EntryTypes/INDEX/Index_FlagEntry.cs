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

            var direction = ctx.Structure.StructureDirection;
            if (direction == TradeDirection.None)
            {
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK] reason=NO_DIRECTION");
                return Reject(ctx, "NO_DIRECTION", 0, TradeDirection.None);
            }

            int barsSinceImpulse = ctx.GetBarsSinceImpulse(direction);
            bool recentDirectionalImpulse = barsSinceImpulse >= 0 && barsSinceImpulse <= 14;
            bool hasImpulse = ctx.Structure.HasImpulse || recentDirectionalImpulse;
            if (!hasImpulse)
            {
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK] reason=NO_IMPULSE");
                return Reject(ctx, "NO_IMPULSE", 0, TradeDirection.None);
            }

            if (!ctx.Structure.HasPullback)
            {
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK] reason=NO_PULLBACK");
                return Reject(ctx, "NO_PULLBACK", 0, TradeDirection.None);
            }

            bool hasFlag = ctx.Structure.HasFlag;
            bool weakButUsableFlag =
                ctx.Structure.FlagBars >= 2 &&
                ctx.Structure.PullbackDepth <= 0.72 &&
                ctx.Structure.FlagCompression <= 0.80;
            if (!hasFlag && !weakButUsableFlag)
            {
                ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][BLOCK] reason=INVALID_FLAG flagBars={ctx.Structure.FlagBars} pullbackDepth={ctx.Structure.PullbackDepth:0.00} compression={ctx.Structure.FlagCompression:0.00}");
                return Reject(ctx, "INVALID_FLAG", 0, TradeDirection.None);
            }

            bool validBreakout =
                (direction == TradeDirection.Long && ctx.Structure.FlagBreakoutUp) ||
                (direction == TradeDirection.Short && ctx.Structure.FlagBreakoutDown);
            bool hasContinuationSignal = ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal;

            bool momentumOk =
                ctx.IsAtrExpanding_M5 ||
                ctx.Adx_M5 >= 17.5 ||
                hasContinuationSignal ||
                validBreakout;
            if (!momentumOk)
            {
                ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][BLOCK] reason=NO_MOMENTUM adx={ctx.Adx_M5:0.0} atrExp={ctx.IsAtrExpanding_M5.ToString().ToLowerInvariant()}");
                return Reject(ctx, "NO_MOMENTUM", 0, TradeDirection.None);
            }

            if (!validBreakout && !hasContinuationSignal)
            {
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK] reason=INVALID_FLAG");
                return Reject(ctx, "INVALID_FLAG", 0, TradeDirection.None);
            }

            double score01 =
                0.5 * ctx.Structure.ImpulseStrength +
                0.3 * (1.0 - ctx.Structure.PullbackDepth) +
                0.2 * (1.0 - ctx.Structure.FlagCompression);

            int score = (int)Math.Round(100.0 * score01);
            if (recentDirectionalImpulse && !ctx.Structure.HasImpulse)
                score -= 8;
            if (!validBreakout && hasContinuationSignal)
                score -= 6;
            if (!ctx.IsAtrExpanding_M5 && ctx.Adx_M5 < 20.0)
                score -= 5;

            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][RECOGNIZED] direction={direction} score={score} breakout={validBreakout.ToString().ToLowerInvariant()} continuation={hasContinuationSignal.ToString().ToLowerInvariant()}");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = score,
                IsValid = true,
                TriggerConfirmed = validBreakout,
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
