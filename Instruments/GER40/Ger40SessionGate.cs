using cAlgo.API;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.GER40
{
    /// <summary>
    /// GER40 SessionGate – Phase 3.8
    /// London-focused index, Asia tiltva.
    /// </summary>
    public class Ger40SessionGate : IGate
    {
        private readonly Robot _bot;

        public Ger40SessionGate(Robot bot)
        {
            _bot = bot;
        }

        public bool AllowEntry(TradeType direction)
        {
            // Broker time
            var t = _bot.Server.Time;
            int h = t.Hour;

            // Asia tiltás
            if (h < 8)
                return false;

            // London + NY
            if (h >= 8 && h < 22)
                return true;

            // === KÉSŐ NY / ROLL-OVER TILTÁS ===
            return false;
        }
    }
}
