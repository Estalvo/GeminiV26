namespace GeminiV26.Core.Entry
{
    public sealed class TransitionParams
    {
        public int MaxImpulseBars { get; init; }
        public double MaxPullbackDepthR { get; init; }
        public int MaxFlagBars { get; init; }
        public double MinCompressionScore { get; init; }
        public double MinImpulseStrength { get; init; }
        public bool StrictWickFilter { get; init; }

        public static TransitionParams ForSymbol(string symbol)
        {
            string sym = (symbol ?? string.Empty).ToUpperInvariant();

            if (sym.Contains("BTC") || sym.Contains("ETH") || sym.Contains("CRYPTO"))
            {
                return new TransitionParams
                {
                    MaxImpulseBars = 5,
                    MaxPullbackDepthR = 0.35,
                    MaxFlagBars = 3,
                    MinCompressionScore = 0.65,
                    MinImpulseStrength = 1.2,
                    StrictWickFilter = true
                };
            }

            if (sym.Contains("NAS") || sym.Contains("USTECH") || sym.Contains("US30") || sym.Contains("GER") || sym.Contains("DAX"))
            {
                return new TransitionParams
                {
                    MaxImpulseBars = 6,
                    MaxPullbackDepthR = 0.38,
                    MaxFlagBars = 4,
                    MinCompressionScore = 0.55,
                    MinImpulseStrength = 1.0,
                    StrictWickFilter = false
                };
            }

            return new TransitionParams
            {
                MaxImpulseBars = 12,
                MaxPullbackDepthR = 0.45,
                MaxFlagBars = 6,
                MinCompressionScore = 0.50,
                MinImpulseStrength = 0.8,
                StrictWickFilter = false
            };
        }
    }
}
