namespace GeminiV26.Core.Entry
{
    public sealed class TransitionEvaluation
    {
        public bool HasImpulse { get; init; }
        public bool HasPullback { get; init; }
        public bool HasFlag { get; init; }

        public int BarsSinceImpulse { get; init; }
        public int PullbackBars { get; init; }
        public int FlagBars { get; init; }

        public double PullbackDepthR { get; init; }
        public double CompressionScore { get; init; }
        public double QualityScore { get; init; }

        public bool IsValid { get; init; }
        public int BonusScore { get; init; }
        public string Reason { get; init; }

        // Backward-compatible alias for existing integrations.
        public bool TransitionValid => IsValid;
    }
}
