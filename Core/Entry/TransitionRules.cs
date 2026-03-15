namespace GeminiV26.Core.Entry
{
    public enum InstrumentType
    {
        FX,
        INDEX,
        CRYPTO,
        METAL
    }

    public sealed class TransitionRules
    {
        public int MaxImpulseAge { get; init; } = 8;
        public double ImpulseMultiplier { get; init; } = 1.2;
        public double MinImpulseBodyRatio { get; init; } = 0.60;
        public double MinImpulseStrength { get; init; } = 0.40;
        public double ImpulseNormalizationAtrFactor { get; init; } = 2.0;

        public double MaxPullbackDepthR { get; init; } = 0.5;
        public int MinPullbackBars { get; init; } = 2;
        public double OptimalPullbackDepthR { get; init; } = 0.42;

        public int MaxFlagBars { get; init; } = 5;
        public double MaxCompressionRatio { get; init; } = 0.75;
        public double StrongAdxThreshold { get; init; } = 30.0;

        public static TransitionRules ForInstrument(InstrumentType type)
        {
            switch (type)
            {
                case InstrumentType.CRYPTO:
                    return new TransitionRules
                    {
                        MaxImpulseAge = 6,

                        ImpulseMultiplier = 1.15,
                        MinImpulseBodyRatio = 0.40,
                        MinImpulseStrength = 0.35,

                        MaxPullbackDepthR = 0.45,
                        MinPullbackBars = 2,
                        OptimalPullbackDepthR = 0.40,

                        MaxFlagBars = 4,
                        MaxCompressionRatio = 0.70,

                        StrongAdxThreshold = 20
                    };

                case InstrumentType.INDEX:
                    return new TransitionRules
                    {
                        MaxImpulseAge = 7,
                        ImpulseMultiplier = 1.3,
                        MinImpulseStrength = 0.40,
                        MaxPullbackDepthR = 0.50,
                        MinPullbackBars = 2,
                        OptimalPullbackDepthR = 0.42,
                        MaxFlagBars = 5,
                        MaxCompressionRatio = 0.80,
                        StrongAdxThreshold = 25
                    };

                case InstrumentType.METAL:
                    return new TransitionRules
                    {
                        MaxImpulseAge = 6,
                        ImpulseMultiplier = 1.35,
                        MinImpulseStrength = 0.45,
                        MaxPullbackDepthR = 0.55,
                        MinPullbackBars = 2,
                        OptimalPullbackDepthR = 0.40,
                        MaxFlagBars = 5,
                        MaxCompressionRatio = 0.75,
                        StrongAdxThreshold = 22
                    };

                default:
                    return new TransitionRules
                    {
                        MaxImpulseAge = 7,
                        ImpulseMultiplier = 1.5,
                        MinImpulseStrength = 0.50,
                        MaxPullbackDepthR = 0.50,
                        MinPullbackBars = 2,
                        OptimalPullbackDepthR = 0.40,
                        MaxFlagBars = 5,
                        MaxCompressionRatio = 0.75,
                        StrongAdxThreshold = 30
                    };
            }
        }
    }
}
