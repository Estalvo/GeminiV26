using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

namespace GeminiV26.Instruments.USDCHF
{
    /// <summary>
    /// USDCHF Session Gate – v3 (NY / Macro-aligned)
    /// UTC-alapú session címkézés
    ///
    /// Asia   : 22:00 – 06:00  (low relevance / noise)
    /// London : 06:00 – 13:00  (build-up)
    /// NewYork: 13:00 – 22:00  (primary)
    ///
    /// Megjegyzés:
    /// - Gate NEM tilt
    /// - Csak session címkét ad vissza
    /// - Döntés az EntryLogic / Matrix szinten történik
    /// </summary>
    public class UsdChfSessionGate : IGate
    {
        private readonly Robot _bot;

        public UsdChfSessionGate(Robot bot)
        {
            _bot = bot;
        }

        // ---------------------------------------------------------
        // Session detektálás (UTC)
        // ---------------------------------------------------------
        public FxSession GetSession()
        {
            int h = _bot.Server.Time.Hour;

            if (h >= 22 || h < 8)
                return FxSession.Asia;

            if (h >= 8 && h < 13)
                return FxSession.London;

            return FxSession.NewYork;         // primary
        }

        // ---------------------------------------------------------
        // Legacy kompatibilitás
        // ---------------------------------------------------------
        public bool AllowEntry(TradeType direction)
        {
            // FX döntés EntryLogic + Matrix alapján
            return true;
        }
    }
}
