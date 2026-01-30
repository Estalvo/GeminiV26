using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

namespace GeminiV26.Instruments.USDJPY
{
    /// <summary>
    /// USDJPY Session Gate – v2 (Tokyo-hangolt)
    /// UTC-alapú session címkézés
    ///
    /// Asia   : 22:00 – 07:00 (Tokyo core)
    /// London : 07:00 – 13:00
    /// NewYork: 13:00 – 22:00
    ///
    /// Megjegyzés:
    /// - Gate NEM tilt, csak session-t ad vissza
    /// - Asia a fő session USDJPY-hez (Tokyo)
    /// - Overlap (13–14) -> NewYork
    /// </summary>
    public class UsdJpySessionGate : IGate
    {
        private readonly Robot _bot;

        public UsdJpySessionGate(Robot bot)
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
