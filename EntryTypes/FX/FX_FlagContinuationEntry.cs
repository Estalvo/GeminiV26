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

            var longEval = EvaluateSide(TradeDirection.Long, ctx);
            var shortEval = EvaluateSide(TradeDirection.Short, ctx);

            if (longEval.IsValid && !shortEval.IsValid)
                return longEval;

            if (!longEval.IsValid && shortEval.IsValid)
                return shortEval;

            if (longEval.IsValid && shortEval.IsValid)
                return longEval.Score >= shortEval.Score ? longEval : shortEval;

            return Invalid(ctx, "NO_VALID_SIDE");
        }

        private EntryEvaluation EvaluateSide(TradeDirection dir, EntryContext ctx)
        {
            int setupScore = 0;

            bool hasImpulse =
                dir == TradeDirection.Long ? ctx.HasImpulseLong_M5 : ctx.HasImpulseShort_M5;

            double pullbackDepthAtr =
                dir == TradeDirection.Long ? ctx.PullbackDepthRLong_M5 : ctx.PullbackDepthRShort_M5;

            bool isValidFlagStructure =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5;

            bool hasValidImpulse =
                hasImpulse ||
                (ctx.IsAtrExpanding_M5 && !ctx.IsRange_M5);

            if (!hasValidImpulse)
                return Invalid(ctx, "NO_IMPULSE");

            if (!isValidFlagStructure)
                return Invalid(ctx, "INVALID_FLAG");

            if (pullbackDepthAtr < MinPullbackAtr)
                return Invalid(ctx, "PB_TOO_SHALLOW");

            // =========================
            // SESSION-AWARE PULLBACK DEPTH
            // =========================
            double maxPb = MaxPullbackAtr;

            if (ctx.Session.ToString() == "NewYork")
                maxPb = 0.75;

            int score = 48;

            if (pullbackDepthAtr > maxPb)
                return Invalid(ctx, "PB_TOO_DEEP");

            if (!ctx.IsPullbackDecelerating_M5)
                return Invalid(ctx, "NO_DECELERATION");

            if (!ctx.HasReactionCandle_M5)
                return Invalid(ctx, "NO_REACTION");

            // ✅ FIX: side-aware M1 confirmation
            bool m1Confirm =
                ctx.M1FlagBreakTrigger ||
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);

            if (!m1Confirm)
                return Invalid(ctx, "NO_M1_CONFIRM");

            bool continuationSignal = m1Confirm;

            bool hasStructure =
                pullbackDepthAtr >= MinPullbackAtr;

            if (!hasStructure)
                setupScore -= 35;
            else
                setupScore += 15;

            bool hasContinuation =
                continuationSignal;

            if (hasContinuation)
                setupScore += 20;

            // ✅ FIX: side-aware score boost
            if (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir)
                score += 10;

            if (ctx.IsRange_M5)
                score -= 10;

            score += setupScore;

            if (setupScore <= 0)
                score = System.Math.Min(score, MinScore - 10);

            if (score < MinScore)
                return Invalid(ctx, "LOW_SCORE");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"FX_FLAG_CONT score={score} pbATR={pullbackDepthAtr:F2}"
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
