using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_FlagContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_FlagContinuation;

        private const int MinScore = 55;
        private const double MinPullbackAtr = 0.15;
        private const double MaxPullbackAtr = 0.60;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, "NO_TREND");

            bool hasValidImpulse =
                ctx.HasImpulse_M5 ||
                (ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5);

            if (!hasValidImpulse)
                return Invalid(ctx, "NO_IMPULSE");

            if (!ctx.IsValidFlagStructure_M5)
                return Invalid(ctx, "INVALID_FLAG");

            if (ctx.PullbackDepthAtr_M5 < MinPullbackAtr)
                return Invalid(ctx, "PB_TOO_SHALLOW");

            // =========================
            // SESSION-AWARE PULLBACK DEPTH
            // =========================
            double maxPb = MaxPullbackAtr;

            if (ctx.Session.ToString() == "NewYork")
                maxPb = 0.75;

            int score = 48;

            if (ctx.PullbackDepthAtr_M5 > maxPb)
                return Invalid(ctx, "PB_TOO_DEEP");

            if (!ctx.IsPullbackDecelerating_M5)
                return Invalid(ctx, "NO_DECELERATION");

            if (!ctx.HasReactionCandle_M5)
                return Invalid(ctx, "NO_REACTION");

            bool m1Confirm =
                ctx.M1FlagBreakTrigger ||
                ctx.M1TriggerInTrendDirection;

            if (!m1Confirm)
                return Invalid(ctx, "NO_M1_CONFIRM");

            if (ctx.M1TriggerInTrendDirection)
                score += 10;

            if (ctx.IsRange_M5)
                score -= 10;

            if (score < MinScore)
                return Invalid(ctx, "LOW_SCORE");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = ctx.TrendDirection,
                Score = score,
                IsValid = true,
                Reason = $"FX_FLAG_CONT score={score} pbATR={ctx.PullbackDepthAtr_M5:F2}"
            };
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
    }
}
