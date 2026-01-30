using cAlgo.API;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.GBPUSD
{
    /// <summary>
    /// GBPUSD Impulse Gate – Phase 3.7
    /// STRUKTÚRA: US30 klón (IGate)
    /// LOGIKA: GBPUSD news/spike elkerülés (szigorúbb, mint index)
    /// </summary>
    public class GbpUsdImpulseGate : IGate
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        // FX: sok “hírgyertya” → legyen szigorúbb
        private const double ImpulseBodyAtrMult = 1.10;
        private const int ImpulseCooldownBars = 3;
        private const double WickDominanceRatio = 0.68;

        public GbpUsdImpulseGate(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;
        }

        public bool AllowEntry(TradeType direction)
        {
            int i = _bars.Count - 1;

            if (IsImpulseBar(i))
                return false;

            if (IsInImpulseCooldown(i))
                return false;

            if (HasDominantWick(i))
                return false;

            return true;
        }

        private bool IsImpulseBar(int i)
        {
            double body = Math.Abs(_bars.ClosePrices[i] - _bars.OpenPrices[i]);

            var atr = _bot.Indicators.AverageTrueRange(_bot.Bars, 14, MovingAverageType.Simple);
            double atrValue = atr.Result[i];

            return atrValue > 0 && body > atrValue * ImpulseBodyAtrMult;
        }

        private bool IsInImpulseCooldown(int i)
        {
            for (int b = 1; b <= ImpulseCooldownBars; b++)
            {
                int idx = i - b;
                if (idx < 0)
                    break;

                if (IsImpulseBar(idx))
                    return true;
            }
            return false;
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
    }
}
