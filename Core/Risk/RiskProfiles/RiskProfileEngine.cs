namespace GeminiV26.Core.Risk.RiskProfiles
{
    public static class RiskProfileEngine
    {
        public static double GetRiskPercent(int finalConfidence)
        {
            if (finalConfidence >= 90) return 0.70;
            if (finalConfidence >= 80) return 0.55;
            if (finalConfidence >= 70) return 0.40;
            return 0.30;
        }
    }
}
