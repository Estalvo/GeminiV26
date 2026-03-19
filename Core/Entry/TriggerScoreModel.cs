using System;

namespace GeminiV26.Core.Entry
{
    public static class TriggerScoreModel
    {
        public static int Apply(
            EntryContext ctx,
            string scope,
            int score,
            bool breakoutDetected,
            bool strongCandle,
            bool followThrough,
            string noTriggerReason = "NO_TRIGGER")
        {
            double triggerScore = 0;

            if (breakoutDetected)
                triggerScore += 1;

            if (strongCandle)
                triggerScore += 1;

            if (followThrough)
                triggerScore += 2;

            score += (int)Math.Round(triggerScore * 5);

            if (triggerScore == 0)
                score -= 15;

            bool minimalTrigger = breakoutDetected || strongCandle;
            if (!minimalTrigger)
                score -= 10;

            ctx?.Log?.Invoke(
                $"[TRIGGER SCORE] scope={scope} breakout={(breakoutDetected ? 1 : 0)} " +
                $"strong={(strongCandle ? 1 : 0)} follow={(followThrough ? 1 : 0)} " +
                $"total={triggerScore:F0} finalScore={score}");

            if (triggerScore == 0 && !string.IsNullOrWhiteSpace(noTriggerReason))
                ctx?.Log?.Invoke($"[TRIGGER SCORE][SOFT] scope={scope} reason={noTriggerReason} impact=score_only");

            return score;
        }
    }
}
