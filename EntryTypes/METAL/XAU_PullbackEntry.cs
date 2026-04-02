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

            if (!ctx.Structure.HasImpulse || !ctx.Structure.HasPullback)
                return Reject(ctx, dir, "NO_IMPULSE_PULLBACK_STRUCTURE", "[ENTRY][XAU_PB][BLOCK_STRUCTURE]");

            int attempts = dir == TradeDirection.Long ? ctx.ContinuationAttemptCountLong : ctx.ContinuationAttemptCountShort;
            if (attempts > 1)
                return Reject(ctx, dir, "REFIRE_BLOCK", "[ENTRY][XAU_PB][REFIRE_BLOCK]");

            bool chop =
                ctx.Adx_M5 < 18 ||
                !ctx.IsAtrExpanding_M5 ||
                (ctx.Structure.PullbackBars >= 3 && !ctx.Structure.PullbackConfirmedSignal);
            if (chop)
                return Reject(ctx, dir, "CHOP_PULLBACK_CLUSTER", "[ENTRY][XAU_PB][CHOP_BLOCK]");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            if (barsSinceImpulse >= 0 && barsSinceImpulse <= FirstWindowMaxBars)
                ctx.Log?.Invoke("[ENTRY][XAU_PB][FIRST_WINDOW]");
            else
                return Reject(ctx, dir, "OUTSIDE_FIRST_PULLBACK_WINDOW", "[ENTRY][XAU_PB][BLOCK_WINDOW]");

            bool depthMature = ctx.Structure.PullbackDepth >= 0.10 && ctx.Structure.PullbackDepth <= 0.75;
            bool continuationAuthority =
                ctx.Structure.PullbackConfirmedSignal &&
                ctx.LastClosedBarInTrendDirection &&
                (ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5);

            if (!depthMature || !continuationAuthority)
                return Reject(ctx, dir, "NO_CONTINUATION_AUTHORITY", "[ENTRY][XAU_PB][BLOCK_CONTINUATION]");

            ctx.Log?.Invoke("[ENTRY][XAU_PB][CONTINUATION_OK]");

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
