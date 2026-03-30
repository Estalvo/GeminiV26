using cAlgo.API;
using GeminiV26.Core.Logging;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.BTCUSD
{
    public class BtcUsdSessionGate : IGate
    {
        private readonly Robot _bot;

        public BtcUsdSessionGate(Robot bot)
        {
            _bot = bot;
        }

        public bool AllowEntry(TradeType direction)
        {
            var utc = _bot.Server.Time.TimeOfDay;

            // Asia 00:00–08:00 UTC
            if (utc >= new TimeSpan(0, 0, 0) &&
                utc <= new TimeSpan(8, 0, 0))
            {
                var dms = _bot.Indicators.DirectionalMovementSystem(14);
                double adx = dms.ADX.LastValue;

                // Asia-ban csak akkor engedünk,
                // ha ERŐS trend van
                if (adx < 28)
                {
                    GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=asia_low_adx_block adx={adx:0.##} symbol={_bot.SymbolName}");
                    return false;
                }
            }

            GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=false reason=session_allow symbol={_bot.SymbolName}");
            return true;
        }
    }
}
