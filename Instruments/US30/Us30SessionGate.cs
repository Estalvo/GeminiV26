using System;
using cAlgo.API;
using GeminiV26.Core.Logging;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.US30
{
    /// <summary>
    /// US30 Session Gate – Phase 3.7
    /// --------------------------------
    /// Rules:
    /// - Asia: BLOCK
    /// - London: allow only AFTER open +30 min
    /// - Pre-NY: BLOCK
    /// - NY: allow only AFTER open +30 min
    /// - NY close -30 min: BLOCK
    /// </summary>
    public class Us30SessionGate : IGate
    {
        private readonly Robot _bot;

        public Us30SessionGate(Robot bot)
        {
            _bot = bot;
        }

        public bool AllowEntry(TradeType direction)
        {
            DateTime utc = _bot.Server.Time;

            TimeSpan t = utc.TimeOfDay;

            // ===== Session times (UTC) =====
            TimeSpan londonOpen = new TimeSpan(8, 0, 0);
            TimeSpan londonTradeStart = new TimeSpan(8, 30, 0);

            TimeSpan nyOpen = new TimeSpan(14, 30, 0);
            TimeSpan nyTradeStart = new TimeSpan(15, 0, 0);
            TimeSpan nyTradeEnd = new TimeSpan(20, 30, 0);

            // ===== Asia: BLOCK =====
            if (t < londonOpen)
            {
                GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_block symbol={_bot.SymbolName}");
                return false;
            }

            // ===== London =====
            if (t >= londonOpen && t < nyOpen)
            {
                // London open +30 min
                if (t < londonTradeStart)
                {
                    GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_block symbol={_bot.SymbolName}");
                    return false;
                }

                // Pre-NY noise block
                GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=false reason=session_allow symbol={_bot.SymbolName}");
                return true;
            }

            // ===== NY =====
            if (t >= nyOpen && t <= nyTradeEnd)
            {
                // NY open +30 min
                if (t < nyTradeStart)
                {
                    GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_block symbol={_bot.SymbolName}");
                    return false;
                }

                GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=false reason=session_allow symbol={_bot.SymbolName}");
                return true;
            }

            // ===== NY close & after =====
            GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_block symbol={_bot.SymbolName}");
            return false;
        }
    }
}
