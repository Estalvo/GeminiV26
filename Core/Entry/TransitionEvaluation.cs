namespace GeminiV26.Core.Entry
{
    public sealed class TransitionEvaluation
    {
        public bool HasImpulse { get; init; }
        public bool HasPullback { get; init; }
        public bool HasFlag { get; init; }

        public int BarsSinceImpulse { get; init; }

        public int PullbackBars { get; init; }
        public double PullbackDepthR { get; init; }

        public int FlagBars { get; init; }
        public double CompressionScore { get; init; }

        public bool TransitionValid { get; init; }

        public int BonusScore { get; init; }

        public string Direction { get; init; }
        public string Reason { get; init; }
    }
}
