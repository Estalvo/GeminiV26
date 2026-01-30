using cAlgo.API;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.AUDUSD
{
    /// <summary>
    /// AUDUSD-specifikus Impulse Gate
    /// - FX-hez hangolt (kevésbé volatilis, mint XAU)
    /// - Pullback / Flag barát
    /// - Spike és noise szűrés
    /// - DEBUG log minden döntésnél
    /// </summary>
    public class AudUsdImpulseGate : IGate
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        // ===== FX-re hangolt paraméterek =====
        private const double ImpulseBodyAtrMult = 1.10;
        private const int ImpulseCooldownBars = 3;
        private const double WickDominanceRatio = 0.55;

        public AudUsdImpulseGate(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;
        }

        public bool AllowEntry(TradeType direction)
        {
            int i = _bars.Count - 1;

            if (HasDominantWick(i))
            {
                _bot.Print("[AUDUSD GATE] BLOCKED: Dominant wick");
                return false;
            }

            if (IsExtremeImpulseBar(i))
            {
                _bot.Print("[AUDUSD GATE] BLOCKED: Extreme impulse bar");
                return false;
            }

            /*if (IsInRecentImpulseCooldown(i))
            {
                _bot.Print("[AUDUSD GATE] BLOCKED: Impulse cooldown");
                return false;
            }
            */

            _bot.Print("[AUDUSD GATE] ALLOWED");
            return true;
        }

        private bool IsExtremeImpulseBar(int i)
        {
            double body = Math.Abs(_bars.ClosePrices[i] - _bars.OpenPrices[i]);
            double atr = GetAtr(i);
            return body > atr * ImpulseBodyAtrMult;
        }

        private bool IsInRecentImpulseCooldown(int i)
        {
            for (int b = 1; b <= ImpulseCooldownBars; b++)
            {
                int idx = i - b;
                if (idx < 0)
                    break;

                if (IsExtremeImpulseBar(idx))
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

        private double GetAtr(int i)
        {
            var atr = _bot.Indicators.AverageTrueRange(
                _bot.Bars,
                14,
                MovingAverageType.Simple
            );

            return atr.Result[i];
        }
    }
}
