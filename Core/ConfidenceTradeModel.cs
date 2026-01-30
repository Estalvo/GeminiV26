namespace GeminiV26.Core
{
    public static class ConfidenceTradeModel
    {
        public static double GetTp1CloseFraction(int confidence)
        {
            if (confidence >= 85) return 0.25; // HIGH: 20–30%
            if (confidence >= 75) return 0.45; // NORMAL: 40–50%
            return 0.65;                       // LOW: 60–70%
        }

        public static TrailingMode GetTrailingMode(int confidence)
        {
            if (confidence >= 85) return TrailingMode.Loose;
            if (confidence >= 75) return TrailingMode.Normal;
            return TrailingMode.Tight;
        }

        public static BeMode GetBeMode(int confidence)
        {
            // LOW + NORMAL: TP1 után BE+buffer
            // HIGH: késleltetett BE
            if (confidence >= 85) return BeMode.Delayed;
            return BeMode.AfterTp1;
        }
    }
}
