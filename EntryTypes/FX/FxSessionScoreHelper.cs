using GeminiV26.Instruments.FX;
using GeminiV26.Core;

namespace GeminiV26.Helpers
{
    public static class FxSessionScoreHelper
    {
        public static int ApplySessionDelta(
            string symbol,
            FxSession session,
            int baseScore)
        {
            var fx = FxInstrumentMatrix.Get(symbol);

            if (fx?.SessionScoreDelta == null)
                return baseScore;

            if (fx.SessionScoreDelta.TryGetValue(session, out var delta))
                return baseScore + delta;

            return baseScore;
        }
    }
}
