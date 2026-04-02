using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_FlagContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_FlagContinuation;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            FxDirectionValidation.LogDirectionDebug(ctx);
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, TradeDirection.None, "CTX_NOT_READY");

            LogStructure(ctx);

            bool hasImpulse = ctx.Structure?.HasImpulse == true;
            bool hasPullback = ctx.Structure?.HasPullback == true;
            bool hasFlag = ctx.Structure?.HasFlag == true;
            var direction = ctx.Structure?.StructureDirection ?? TradeDirection.None;
            bool breakoutUp = ctx.Structure?.FlagBreakoutUp == true;
            bool breakoutDown = ctx.Structure?.FlagBreakoutDown == true;

            ctx.Log?.Invoke(
                $"[FX][FLAG_CHECK] impulse={hasImpulse.ToString().ToLowerInvariant()} pullback={hasPullback.ToString().ToLowerInvariant()} flag={hasFlag.ToString().ToLowerInvariant()} direction={direction} breakout_up={breakoutUp.ToString().ToLowerInvariant()} breakout_down={breakoutDown.ToString().ToLowerInvariant()}");

            if (!hasImpulse)
                return Block(ctx, "NO_IMPULSE");

            if (!hasPullback)
                return Block(ctx, "NO_PULLBACK");

            if (!hasFlag)
                return Block(ctx, "NO_FLAG");

            if (direction == TradeDirection.None)
                return Block(ctx, "NO_DIRECTION");

            if (direction == TradeDirection.Long && !breakoutUp)
                return Block(ctx, "NO_BREAKOUT_UP");

            if (direction == TradeDirection.Short && !breakoutDown)
                return Block(ctx, "NO_BREAKOUT_DOWN");

            if ((direction == TradeDirection.Long && breakoutDown) || (direction == TradeDirection.Short && breakoutUp))
                return Block(ctx, "DIRECTION_MISMATCH");

            ctx.Log?.Invoke($"[FX][FLAG_ENTRY] direction={direction}");

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = 70,
                IsValid = true,
                TriggerConfirmed = true,
                Reason = "FX_FLAG_STRUCTURE_CONFIRMED"
            };

            EntryDirectionQuality.LogDecision(
                ctx,
                Type.ToString(),
                direction == TradeDirection.Long ? eval : null,
                direction == TradeDirection.Short ? eval : null,
                eval.Direction);

            return EntryDecisionPolicy.Normalize(eval);
        }

        private void LogStructure(EntryContext ctx)
        {
            ctx.Log?.Invoke(
                $"[FX][STRUCTURE] impulse={(ctx.Structure?.HasImpulse == true).ToString().ToLowerInvariant()} pullback={(ctx.Structure?.HasPullback == true).ToString().ToLowerInvariant()} micro_pullback={(ctx.Structure?.HasMicroPullback == true).ToString().ToLowerInvariant()} flag={(ctx.Structure?.HasFlag == true).ToString().ToLowerInvariant()} direction={ctx.Structure?.StructureDirection ?? TradeDirection.None}");
        }

        private EntryEvaluation Block(EntryContext ctx, string reason)
        {
            ctx.Log?.Invoke($"[FX][ENTRY_BLOCK] reason={reason}");
            return Invalid(ctx, TradeDirection.None, reason);
        }

        private EntryEvaluation Invalid(EntryContext ctx, TradeDirection dir, string reason)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = 0,
                IsValid = false,
                Reason = reason
            };
    }
}
