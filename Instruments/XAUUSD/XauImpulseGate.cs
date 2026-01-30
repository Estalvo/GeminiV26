using cAlgo.API;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.XAUUSD
{
    /// <summary>
    /// XAU-specifikus Impulse Gate
    /// - Flag breakout BARÁTSÁGOS
    /// - Spike / noise szűrés
    /// - Rövid, kontrollált cooldown
    /// - Részletes DEBUG log
    /// </summary>
    public class XauImpulseGate : IGate
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        // ===== XAU-ra hangolt paraméterek =====
        private const double ImpulseBodyAtrMult = 1.8;   // csak extrém spike tilt
        private const int ImpulseCooldownBars = 1;        // rövid cooldown
        private const double WickDominanceRatio = 0.80;   // XAU wick-heavy tolerancia

        public XauImpulseGate(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;
        }

        public bool AllowEntry(TradeType direction)
        {
            int i = _bars.Count - 1;

            // ===== 1️⃣ Wick-dominancia =====
            if (HasDominantWick(i))
            {
                _bot.Print($"[XAU GATE] BLOCKED: Dominant wick (ratio>{WickDominanceRatio})");
                return false;
            }

            // ===== 2️⃣ Extrém impulse (spike) =====
            if (IsExtremeImpulseBar(i))
            {
                _bot.Print($"[XAU GATE] BLOCKED: Extreme impulse bar (body > ATR*{ImpulseBodyAtrMult})");
                return false;
            }

            // ===== 3️⃣ Rövid impulse cooldown =====
            if (IsInRecentImpulseCooldown(i))
            {
                _bot.Print($"[XAU GATE] BLOCKED: Impulse cooldown ({ImpulseCooldownBars} bars)");
                return false;
            }

            _bot.Print("[XAU GATE] ALLOWED");
            return true;
        }

        // =========================================================
        // Helperek
        // =========================================================

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
