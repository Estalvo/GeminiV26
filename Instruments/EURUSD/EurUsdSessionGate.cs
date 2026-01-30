using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

namespace GeminiV26.Instruments.EURUSD
{
    /// <summary>
    /// EURUSD Session Gate – v2
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
    public class EurUsdSessionGate : IGate
    {
        private readonly Robot _bot;

        public EurUsdSessionGate(Robot bot)
        {
            _bot = bot;
        }

        // ---------------------------------------------------------
        // ÚJ: Session detektálás (FX architektúra ezt használja)
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
        // Legacy kompatibilitás (FX-nél NEM ez dönt)
        // ---------------------------------------------------------
        public bool AllowEntry(TradeType direction)
        {
            // FX-nél a döntés az EntryType-ban történik session alapján
            // Itt mindig true, hogy ne törjük a régi hívási láncot
            return true;
        }
    }
}
