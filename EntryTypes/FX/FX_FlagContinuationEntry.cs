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
            bool alignedBreakout = direction == TradeDirection.Long ? breakoutUp : breakoutDown;
            bool opposingBreakout = direction == TradeDirection.Long ? breakoutDown : breakoutUp;
            bool hasContinuationSignal = ctx.Structure?.ContinuationEarlySignal == true || ctx.Structure?.ContinuationConfirmedSignal == true;
            int barsSinceImpulse = ctx.GetBarsSinceImpulse(direction);
            bool hasRecentDirectionalImpulse = direction != TradeDirection.None && barsSinceImpulse >= 0 && barsSinceImpulse <= 12;

            ctx.Log?.Invoke(
                $"[FX][FLAG_CHECK] impulse={hasImpulse.ToString().ToLowerInvariant()} pullback={hasPullback.ToString().ToLowerInvariant()} flag={hasFlag.ToString().ToLowerInvariant()} direction={direction} breakout_up={breakoutUp.ToString().ToLowerInvariant()} breakout_down={breakoutDown.ToString().ToLowerInvariant()}");

            if (direction != TradeDirection.Long && direction != TradeDirection.Short)
                return Block(ctx, "NO_DIRECTION");

            bool localAuthority =
                hasPullback &&
                hasFlag &&
                hasContinuationSignal &&
                (alignedBreakout || !opposingBreakout);

            if (!hasImpulse && !hasRecentDirectionalImpulse)
                return Block(ctx, "NO_IMPULSE");

            if (!hasPullback)
                return Block(ctx, "NO_PULLBACK");

            if (!hasFlag)
                return Block(ctx, "INVALID_FLAG");

            if (!alignedBreakout && !hasContinuationSignal)
                return Block(ctx, "INVALID_FLAG");

            if (opposingBreakout)
                return Block(ctx, "DIRECTION_MISMATCH");

            var timing = ContinuationTimingGate.Evaluate(ctx, direction, Type.ToString());
            if (!timing.IsAllowed && timing.Reason == "TIMING_SIDE_INACTIVE" && !localAuthority)
                return Block(ctx, "TIMING_SIDE_INACTIVE");

            int score = 70;
            if (hasRecentDirectionalImpulse && !hasImpulse)
            {
                score -= 6;
                ctx.Log?.Invoke($"[ENTRY][FX_FLAG][PASS] reason=RECENT_IMPULSE_FALLBACK barsSinceImpulse={barsSinceImpulse}");
            }

            if (!timing.IsAllowed && timing.Reason == "TIMING_SIDE_INACTIVE" && localAuthority)
            {
                score -= 8;
                ctx.Log?.Invoke($"[ENTRY][FX_FLAG][PASS] reason=TIMING_SIDE_INACTIVE_LOCAL_AUTHORITY direction={direction}");
            }
            else
            {
                score += timing.ScoreAdjustment;
            }

            var htfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                htfDir != TradeDirection.None &&
                direction != TradeDirection.None &&
                htfDir != direction;

            if (htfMismatch && !localAuthority)
                return Block(ctx, "HTF_MISMATCH");

            if (htfMismatch)
            {
                score -= 10;
                ctx.Log?.Invoke($"[ENTRY][FX_FLAG][PASS] reason=HTF_MISMATCH_LOCAL_AUTHORITY htf={htfDir} dir={direction}");
            }

            score = score < 0 ? 0 : (score > 100 ? 100 : score);
            ctx.Log?.Invoke($"[ENTRY][FX_FLAG][RECOGNIZED] direction={direction} score={score} localAuthority={localAuthority.ToString().ToLowerInvariant()}");

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = score,
                IsValid = true,
                TriggerConfirmed = alignedBreakout,
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
            ctx.Log?.Invoke($"[ENTRY][FX_FLAG][BLOCK] reason={reason}");
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
