using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.Core;

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
            //_bot.Print("[BTC SESSION] ALLOWED");
            return true;
        }
    }
}
