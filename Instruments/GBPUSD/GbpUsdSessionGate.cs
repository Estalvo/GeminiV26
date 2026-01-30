using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

namespace GeminiV26.Instruments.GBPUSD
{
    /// <summary>
    /// GBPUSD Session Gate – v2
    /// UTC-alapú session címkézés
    ///
    /// Asia   : 22:00 – 07:00
    /// London : 07:00 – 13:00
    /// NewYork: 13:00 – 22:00
    ///
    /// Megjegyzés:
    /// - Gate NEM tilt, csak session-t ad vissza
    /// - Overlap (13–14) -> NewYork
    /// </summary>
    public class GbpUsdSessionGate : IGate
    {
        private readonly Robot _bot;

        public GbpUsdSessionGate(Robot bot)
        {
            _bot = bot;
        }

        // ---------------------------------------------------------
        // Session detektálás
        // ---------------------------------------------------------
        public FxSession GetSession()
        {
            int h = _bot.Server.Time.Hour;

            if (h >= 22 || h < 8)
                return FxSession.Asia;

            if (h >= 8 && h < 13)
                return FxSession.London;

            return FxSession.NewYork;
        }

        // ---------------------------------------------------------
        // Legacy kompatibilitás
        // ---------------------------------------------------------
        public bool AllowEntry(TradeType direction)
        {
            // FX-nél a döntés az EntryType-ban történik
            return true;
        }
    }
}
