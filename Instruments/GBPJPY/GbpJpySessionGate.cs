using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

namespace GeminiV26.Instruments.GBPJPY
{
    /// <summary>
    /// GBPJPY Session Gate – v2 (Tokyo-hangolt)
    /// UTC-alapú session címkézés
    ///
    /// Asia   : 22:00 – 07:00 (Tokyo core)
    /// London : 07:00 – 13:00
    /// NewYork: 13:00 – 22:00
    ///
    /// Megjegyzés:
    /// - Gate NEM tilt, csak session-t ad vissza
    /// - Asia a fő session AUDUSD-hez (Tokyo)
    /// - Overlap (13–14) -> NewYork
    /// </summary>
    public class GbpJpySessionGate : IGate
    {
        private readonly Robot _bot;

        public GbpJpySessionGate(Robot bot)
        {
            _bot = bot;
        }

        // ---------------------------------------------------------
        // Session detektálás
        // ---------------------------------------------------------
        public FxSession GetSession()
        {
            int h = _bot.Server.Time.Hour;

            if (h >= 22 || h < 7)
                return FxSession.Asia;      // Tokyo core

            if (h >= 7 && h < 13)
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
