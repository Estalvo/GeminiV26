using System;

namespace GeminiV26.Core.Entry
{
    internal enum ContinuationTimingDecision
    {
        None = 0,
        Early = 1,
        LateValid = 2,
        LateReject = 3,
        OverextendedReject = 4,
        SideInactiveReject = 5
    }

    internal readonly struct ContinuationTimingResult
    {
        public ContinuationTimingResult(
            ContinuationTimingDecision decision,
            bool isAllowed,
            int scoreAdjustment,
            int minScoreAdjustment,
            bool requireStrongTrigger,
            bool requireStrongStructure,
            string reason)
        {
            Decision = decision;
            IsAllowed = isAllowed;
            ScoreAdjustment = scoreAdjustment;
            MinScoreAdjustment = minScoreAdjustment;
            RequireStrongTrigger = requireStrongTrigger;
            RequireStrongStructure = requireStrongStructure;
            Reason = reason;
        }

        public ContinuationTimingDecision Decision { get; }
        public bool IsAllowed { get; }
        public int ScoreAdjustment { get; }
        public int MinScoreAdjustment { get; }
        public bool RequireStrongTrigger { get; }
        public bool RequireStrongStructure { get; }
        public string Reason { get; }
    }

    internal static class ContinuationTimingGate
    {
        private const int MissedLateBarsThreshold = 6;
        private const double MissedLateFreshnessThreshold = 0.45;

        public static ContinuationTimingResult Evaluate(EntryContext ctx, TradeDirection direction, string entryType)
        {
            bool isLong = direction == TradeDirection.Long;
            bool isSideActive = isLong ? ctx.IsTimingLongActive : ctx.IsTimingShortActive;

            bool hasFreshPullback = isLong ? ctx.HasFreshPullbackLong : ctx.HasFreshPullbackShort;
            bool hasEarlyContinuation = isLong ? ctx.HasEarlyContinuationLong : ctx.HasEarlyContinuationShort;
            bool hasLateContinuation = isLong ? ctx.HasLateContinuationLong : ctx.HasLateContinuationShort;
            bool isOverextended = isLong ? ctx.IsOverextendedLong : ctx.IsOverextendedShort;

            int continuationAttempts = isLong ? ctx.ContinuationAttemptCountLong : ctx.ContinuationAttemptCountShort;
            int barsSinceImpulse = isLong ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            double freshness = isLong ? ctx.ContinuationFreshnessLong : ctx.ContinuationFreshnessShort;

            if (double.IsNaN(freshness) || double.IsInfinity(freshness))
                freshness = 0.0;

            if (!isSideActive)
            {
                Log(ctx, "LATE_REJECT", direction, entryType, isSideActive, hasFreshPullback, hasEarlyContinuation, hasLateContinuation, continuationAttempts, barsSinceImpulse, freshness, "TIMING_SIDE_INACTIVE");
                return new ContinuationTimingResult(ContinuationTimingDecision.SideInactiveReject, false, 0, 0, false, false, "TIMING_SIDE_INACTIVE");
            }

            barsSinceImpulse = Math.Max(0, barsSinceImpulse);
            continuationAttempts = Math.Max(0, continuationAttempts);

            if (isOverextended)
            {
                Log(ctx, "OVEREXTENDED", direction, entryType, isSideActive, hasFreshPullback, hasEarlyContinuation, hasLateContinuation, continuationAttempts, barsSinceImpulse, freshness, "OVEREXTENDED_HARD_REJECT");
                return new ContinuationTimingResult(ContinuationTimingDecision.OverextendedReject, false, 0, 0, false, false, "OVEREXTENDED_HARD_REJECT");
            }

            if (hasFreshPullback || hasEarlyContinuation)
            {
                Log(ctx, "EARLY", direction, entryType, isSideActive, hasFreshPullback, hasEarlyContinuation, hasLateContinuation, continuationAttempts, barsSinceImpulse, freshness, "EARLY_CONTINUATION_WINDOW");
                return new ContinuationTimingResult(ContinuationTimingDecision.Early, true, 6, -4, false, false, "EARLY_CONTINUATION_WINDOW");
            }

            if (hasLateContinuation)
            {
                bool likelyMissed =
                    (continuationAttempts > 1 && freshness < MissedLateFreshnessThreshold) ||
                    (barsSinceImpulse > MissedLateBarsThreshold && freshness < 0.40);

                if (likelyMissed)
                {
                    Log(ctx, "LATE_REJECT", direction, entryType, isSideActive, hasFreshPullback, hasEarlyContinuation, hasLateContinuation, continuationAttempts, barsSinceImpulse, freshness, "LATE_MISSED_EARLY_CHASING");
                    return new ContinuationTimingResult(ContinuationTimingDecision.LateReject, false, 0, 0, false, false, "LATE_MISSED_EARLY_CHASING");
                }

                Log(ctx, "LATE_VALID", direction, entryType, isSideActive, hasFreshPullback, hasEarlyContinuation, hasLateContinuation, continuationAttempts, barsSinceImpulse, freshness, "LATE_VALID_NO_MISSED_EARLY");
                return new ContinuationTimingResult(ContinuationTimingDecision.LateValid, true, -6, 4, true, true, "LATE_VALID_NO_MISSED_EARLY");
            }

            return new ContinuationTimingResult(ContinuationTimingDecision.None, true, 0, 0, false, false, "NO_TIMING_WINDOW");
        }

        private static void Log(
            EntryContext ctx,
            string tag,
            TradeDirection direction,
            string entryType,
            bool isSideActive,
            bool hasFreshPullback,
            bool hasEarlyContinuation,
            bool hasLateContinuation,
            int continuationAttempts,
            int barsSinceImpulse,
            double freshness,
            string reason)
        {
            ctx.Log?.Invoke(
                $"[ENTRY][TIMING][{tag}] symbol={ctx.Symbol} direction={direction} entryType={entryType} " +
                $"isTimingSideActive={isSideActive.ToString().ToLowerInvariant()} freshPullback={hasFreshPullback.ToString().ToLowerInvariant()} " +
                $"early={hasEarlyContinuation.ToString().ToLowerInvariant()} late={hasLateContinuation.ToString().ToLowerInvariant()} " +
                $"attempts={continuationAttempts} barsSinceImpulse={barsSinceImpulse} freshness={freshness:0.00} reason={reason}");
        }
    }
}
