using cAlgo.API;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdSessionGate : IGate
    {
        private readonly Robot _bot;

        public EthUsdSessionGate(Robot bot)
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
                    return false;
            }

            return true;
        }
    }
}
