using cAlgo.API;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdImpulseGate : IGate
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        // ETH baseline (később finomhangoljuk)
        private const double ImpulseBodyAtrMult = 1.30;
        private const double WickDominanceRatio = 0.65;

        public EthUsdImpulseGate(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;
        }

        public bool AllowEntry(TradeType direction)
        {
            int i = _bars.Count - 1;

            if (HasDominantWick(i))
            {
                _bot.Print("[ETH GATE] BLOCKED: Dominant wick");
                return false;
            }

            if (IsExtremeImpulseBar(i))
            {
                _bot.Print("[ETH GATE] BLOCKED: Extreme impulse bar");
                return false;
            }

            _bot.Print("[ETH GATE] ALLOWED");
            return true;
        }

        private bool IsExtremeImpulseBar(int i)
        {
            double body = Math.Abs(_bars.ClosePrices[i] - _bars.OpenPrices[i]);
            double atr = GetAtr(i);
            return body > atr * ImpulseBodyAtrMult;
        }

        private bool HasDominantWick(int i)
        {
            double high = _bars.HighPrices[i];
            double low = _bars.LowPrices[i];
            double open = _bars.OpenPrices[i];
            double close = _bars.ClosePrices[i];

            double range = high - low;
            if (range <= 0)
                return false;

            double upperWick = high - Math.Max(open, close);
            double lowerWick = Math.Min(open, close) - low;
            double dominant = Math.Max(upperWick, lowerWick);

            return (dominant / range) > WickDominanceRatio;
        }

        private double GetAtr(int i)
        {
            var atr = _bot.Indicators.AverageTrueRange(_bot.Bars, 14, MovingAverageType.Simple);
            return atr.Result[i];
        }
    }
}
