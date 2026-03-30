using cAlgo.API;
using GeminiV26.Core.Logging;
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
            {
                GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_block symbol={_bot.SymbolName}");
                return false;
            }

            // London + NY
            if (h >= 8 && h < 22)
            {
                GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=false reason=session_allow symbol={_bot.SymbolName}");
                return true;
            }

            // === KÉSŐ NY / ROLL-OVER TILTÁS ===
            GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_block symbol={_bot.SymbolName}");
            return false;
        }
    }
}
