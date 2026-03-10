namespace GeminiV26.Core.Entry
{
    public sealed class TransitionRules
    {
        public int MaxImpulseAge { get; init; } = 8;
        public double ImpulseMultiplier { get; init; } = 1.2;
        public double MinImpulseBodyRatio { get; init; } = 0.60;

        public double MaxPullbackDepthR { get; init; } = 0.5;
        public int MinPullbackBars { get; init; } = 2;

        public int MaxFlagBars { get; init; } = 5;
        public double MaxCompressionRatio { get; init; } = 0.75;

        public static TransitionRules ForSymbol(string symbol)
        {
            var sym = (symbol ?? string.Empty).ToUpperInvariant();

            if (sym.Contains("BTC") || sym.Contains("ETH") || sym.Contains("CRYPTO"))
            {
                return new TransitionRules
                {
                    MaxImpulseAge = 6,
                    ImpulseMultiplier = 1.4,
                    MaxPullbackDepthR = 0.45,
                    MinPullbackBars = 2,
                    MaxFlagBars = 4,
                    MaxCompressionRatio = 0.70
                };
            }

            if (sym.Contains("NAS") || sym.Contains("US30") || sym.Contains("GER") || sym.Contains("DAX"))
            {
                return new TransitionRules
                {
                    MaxImpulseAge = 7,
                    ImpulseMultiplier = 1.3,
                    MaxPullbackDepthR = 0.5,
                    MinPullbackBars = 2,
                    MaxFlagBars = 5,
                    MaxCompressionRatio = 0.80
                };
            }

            return new TransitionRules();
        }
    }
}
