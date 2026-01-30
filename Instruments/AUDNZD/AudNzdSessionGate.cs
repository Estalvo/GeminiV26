using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

namespace GeminiV26.Instruments.AUDNZD
{
    /// <summary>
    /// AUDNZD Session Gate – v2 (Tokyo-hangolt)
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
    public class AudNzdSessionGate : IGate
    {
        private readonly Robot _bot;

        public AudNzdSessionGate(Robot bot)
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
                return FxSession.Asia;      // Tokyo core + transition

            if (h >= 8 && h < 13)
                return FxSession.London;    // Valódi London FX

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
