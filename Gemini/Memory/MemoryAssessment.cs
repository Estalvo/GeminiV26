namespace Gemini.Memory
{
    public sealed class MemoryAssessment
    {
        public bool IsLateMove { get; set; }
        public bool IsChaseRisk { get; set; }
        public double ContextTrustScore { get; set; }
        public int RecommendedPenalty { get; set; }
    }
}
