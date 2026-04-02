using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.EntryTypes;

namespace GeminiV26.EntryTypes.METAL
{
    public sealed class XAU_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Pullback;

        private const int MinBars = 20;
        private const int FirstWindowMaxBars = 4;
        private const int NearFirstWindowMaxBars = 6;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Reject(ctx, TradeDirection.None, "SESSION_DISABLED", "[ENTRY][XAU_PB][BLOCK_SESSION]");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < MinBars)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY", "[ENTRY][XAU_PB][BLOCK_CTX]");

            var dir = ctx.LogicBiasDirection;
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_LOGIC_BIAS", "[ENTRY][XAU_PB][BLOCK_DIR]");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            bool recentImpulseWidened =
                !ctx.Structure.HasImpulse &&
                barsSinceImpulse >= 0 &&
                barsSinceImpulse <= 8 &&
                ctx.Structure.ImpulseStrength >= 0.50;
            bool shallowPullbackWidened =
                !ctx.Structure.HasPullback &&
                ctx.Structure.HasMicroPullback &&
                ctx.Structure.PullbackDepth >= 0.06 &&
                ctx.Structure.PullbackDepth <= 0.24 &&
                (ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal || ctx.HasReactionCandle_M5);
            if ((!ctx.Structure.HasImpulse && !recentImpulseWidened) || (!ctx.Structure.HasPullback && !shallowPullbackWidened))
                return Reject(ctx, dir, "NO_IMPULSE_PULLBACK_STRUCTURE", "[ENTRY][XAU_PB][WIDEN_STILL_BLOCK][CODE=NO_IMPULSE_PULLBACK_STRUCTURE]");
            if (recentImpulseWidened)
                ctx.Log?.Invoke($"[ENTRY][XAU_PB][WIDEN_ALLOW] code=RECENT_IMPULSE barsSinceImpulse={barsSinceImpulse}");
            if (shallowPullbackWidened)
                ctx.Log?.Invoke($"[ENTRY][XAU_PB][WIDEN_ALLOW] code=SHALLOW_PULLBACK depth={ctx.Structure.PullbackDepth:0.00}");

            bool trendStateOk = ctx.QualificationState?.HasTrend == true;
            bool localContinuationStructure =
                ctx.Structure.ImpulseStrength >= 0.50 &&
                ctx.Structure.PullbackDepth >= 0.10 &&
                ctx.Structure.PullbackDepth <= 0.75 &&
                (ctx.Structure.PullbackConfirmedSignal || ctx.Structure.ContinuationConfirmedSignal);
            if (!trendStateOk && !localContinuationStructure)
                return Reject(ctx, dir, "NO_TREND_STATE", "[ENTRY][XAU_PB][BLOCK] reason=NO_TREND_STATE");
            if (!trendStateOk && localContinuationStructure)
                ctx.Log?.Invoke("[ENTRY][XAU_PB][RECOGNIZED] reason=NO_TREND_STATE_BYPASS_LOCAL_CONTINUATION");

            int attempts = dir == TradeDirection.Long ? ctx.ContinuationAttemptCountLong : ctx.ContinuationAttemptCountShort;
            if (attempts > 1)
                return Reject(ctx, dir, "REFIRE_BLOCK", "[ENTRY][XAU_PB][REFIRE_BLOCK]");

            bool hardChop =
                ctx.Adx_M5 < 16 ||
                (ctx.Adx_M5 < 18 &&
                 !ctx.IsAtrExpanding_M5 &&
                 ctx.Structure.PullbackBars >= 4 &&
                 !ctx.Structure.PullbackConfirmedSignal);
            if (hardChop)
                return Reject(ctx, dir, "CHOP_BLOCK", "[ENTRY][XAU_PB][BLOCK] reason=CHOP_BLOCK");

            bool widenedChopPass =
                ctx.Adx_M5 >= 16 &&
                ctx.Adx_M5 < 18 &&
                !ctx.IsAtrExpanding_M5 &&
                ctx.Structure.PullbackBars <= 3;
            if (widenedChopPass)
                ctx.Log?.Invoke("[ENTRY][XAU_PB][RECOGNIZED] reason=CHOP_FILTER_WIDENED");

            if (barsSinceImpulse >= 0 && barsSinceImpulse <= FirstWindowMaxBars)
                ctx.Log?.Invoke("[ENTRY][XAU_PB][FIRST_WINDOW]");
            else if (barsSinceImpulse > FirstWindowMaxBars &&
                     barsSinceImpulse <= NearFirstWindowMaxBars &&
                     ctx.Structure.ImpulseStrength >= 0.60 &&
                     (ctx.Structure.PullbackConfirmedSignal || ctx.HasReactionCandle_M5))
                ctx.Log?.Invoke("[ENTRY][XAU_PB][RECOGNIZED] reason=NEAR_FIRST_WINDOW");
            else
                return Reject(ctx, dir, "LATE_BLOCK", "[ENTRY][XAU_PB][BLOCK] reason=LATE_BLOCK");

            bool depthMature = ctx.Structure.PullbackDepth >= 0.10 && ctx.Structure.PullbackDepth <= 0.75;
            bool depthMatureWidened =
                ctx.Structure.PullbackDepth >= 0.08 &&
                ctx.Structure.PullbackDepth <= 0.82 &&
                ctx.Structure.ImpulseStrength >= 0.55 &&
                ctx.Structure.PullbackBars <= 4;
            if (!depthMature && !depthMatureWidened)
                return Reject(ctx, dir, "DEPTH_INVALID", "[ENTRY][XAU_PB][BLOCK] reason=DEPTH_INVALID");

            bool continuationAuthority =
                ctx.Structure.PullbackConfirmedSignal &&
                ctx.LastClosedBarInTrendDirection &&
                (ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5);
            bool continuationAuthorityWidened =
                ctx.LastClosedBarInTrendDirection &&
                (ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5) &&
                (ctx.Structure.PullbackConfirmedSignal || ctx.Structure.ContinuationConfirmedSignal);

            if (!continuationAuthority && !continuationAuthorityWidened)
                return Reject(ctx, dir, "CONTINUATION_AUTHORITY_FAIL", "[ENTRY][XAU_PB][BLOCK] reason=CONTINUATION_AUTHORITY_FAIL");

            ctx.Log?.Invoke("[ENTRY][XAU_PB][CONTINUATION_OK]");
            ctx.Log?.Invoke(
                depthMature
                    ? "[ENTRY][XAU_PB][RECOGNIZED] reason=STRUCTURE_FIRST_OK"
                    : "[ENTRY][XAU_PB][RECOGNIZED] reason=DEPTH_WIDENED");

            int score = 62;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.Structure.PullbackConfirmedSignal)
                score += 8;
            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = "XAU_PULLBACK_STRUCTURE_FIRST_OK"
            };
        }

        private static EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, string reason, string log)
        {
            ctx?.Log?.Invoke(log);
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Pullback,
                Direction = dir,
                IsValid = false,
                Score = 0,
                Reason = reason
            };
        }
    }
}
