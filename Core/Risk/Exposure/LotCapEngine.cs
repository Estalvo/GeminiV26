namespace GeminiV26.Core.Risk.Exposure
{
    public static class LotCapEngine
    {
        public const double ReferenceBalance = 10000.0;
        public const double ReferenceSlDistance = 3.0;

        public static double CalculateLotCap(
            double balance,
            double slDistance,
            double riskPercent)
        {
            if (balance <= 0 || slDistance <= 0 || riskPercent <= 0)
                return 0;

            double riskAmount = balance * riskPercent / 100.0;
            double riskPosition = riskAmount / slDistance;
            double cap = riskPosition * 1.2;

            return cap;
        }
    }
}
