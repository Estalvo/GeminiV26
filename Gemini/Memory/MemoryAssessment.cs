namespace Gemini.Memory
{
    public sealed class MemoryAssessment
    {
        public bool IsLateMove { get; set; }
        public bool IsLateContinuation { get; set; }
        public bool IsExhaustedContinuation { get; set; }
        public bool IsOverextendedMove { get; set; }
        public bool IsFirstPullbackWindow { get; set; }
        public bool IsEarlyContinuationWindow { get; set; }
        public bool IsMatureContinuationWindow { get; set; }
        public bool IsChaseRisk { get; set; }
        public double ContextTrustScore { get; set; }
        public int RecommendedPenalty { get; set; }
        public int RecommendedTimingPenalty { get; set; }
    }
}
