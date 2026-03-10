namespace GeminiV26.Core.Matrix
{
    public sealed class SessionMatrixConfig
    {
        public bool AllowFlag { get; set; }
        public bool AllowPullback { get; set; }
        public bool AllowBreakout { get; set; }

        public double MinAtrMultiplier { get; set; }
        public double MinAdx { get; set; }
        public double MinEmaDistance { get; set; }

        public double EntryScoreModifier { get; set; }
    }

    public sealed class SessionMatrixContext
    {
        public SessionBucket Bucket { get; set; }
        public SessionPriority Priority { get; set; }
        public TimeframeTier Tier { get; set; }
        public string InstrumentClass { get; set; }
    }
}
