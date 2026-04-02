using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_ImpulseContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_ImpulseContinuation;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            FxDirectionValidation.LogDirectionDebug(ctx);
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, TradeDirection.None, "CTX_NOT_READY");

            LogStructure(ctx);

            var structureDirection = ctx.Structure?.StructureDirection ?? TradeDirection.None;
            if (structureDirection != TradeDirection.Long && structureDirection != TradeDirection.Short)
                return Block(ctx, "NO_DIRECTION");

            bool hasImpulse = ctx.Structure?.HasImpulse == true;
            bool hasPullback = ctx.Structure?.HasPullback == true;
            bool hasMicroPullback = ctx.Structure?.HasMicroPullback == true;
            bool early = ctx.Structure?.ContinuationEarlySignal == true;
            bool confirmed = ctx.Structure?.ContinuationConfirmedSignal == true;

            ctx.Log?.Invoke(
                $"[FX][IMPULSE_CHECK] impulse={hasImpulse.ToString().ToLowerInvariant()} micro_pullback={hasMicroPullback.ToString().ToLowerInvariant()} direction={structureDirection} early={early.ToString().ToLowerInvariant()} confirmed={confirmed.ToString().ToLowerInvariant()}");

            if (!hasImpulse)
                return Block(ctx, "NO_IMPULSE");

            if (!hasPullback)
                return Block(ctx, "NO_PULLBACK");

            if (!hasMicroPullback)
                return Block(ctx, "NO_MICRO_PULLBACK");

            if (!early && !confirmed)
                return Block(ctx, "NO_CONTINUATION_SIGNAL");

            string entryType = confirmed ? "CONFIRMED" : "EARLY";
            ctx.Log?.Invoke($"[FX][IMPULSE_ENTRY] type={entryType} direction={structureDirection}");

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = structureDirection,
                Score = confirmed ? 70 : 60,
                IsValid = true,
                TriggerConfirmed = confirmed,
                Reason = $"FX_IMPULSE_STRUCTURE_{entryType}"
            };

            EntryDirectionQuality.LogDecision(
                ctx,
                Type.ToString(),
                structureDirection == TradeDirection.Long ? eval : null,
                structureDirection == TradeDirection.Short ? eval : null,
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
