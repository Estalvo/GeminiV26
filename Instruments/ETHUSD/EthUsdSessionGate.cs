using cAlgo.API;
using GeminiV26.Interfaces;

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
            //_bot.Print("[ETH SESSION] ALLOWED");
            return true;
        }
    }
}
