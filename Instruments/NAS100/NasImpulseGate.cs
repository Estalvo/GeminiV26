using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.NAS100
{
    /// <summary>
    /// NAS100 Impulse Gate (SOFT)
    /// --------------------------
    /// Cél:
    /// - extrém spike-ok szűrése
    /// - manipulációs wickek kizárása
    /// - normál NAS impulzusok ENGEDÉSE
    ///
    /// FONTOS:
    /// - NINCS body-only tilt
    /// - csak EXTREME + WICK együtt blokkol
    /// - rövid cooldown
    /// </summary>
    public class NasImpulseGate : IGate
    {
        private readonly Robot _bot;
        private readonly Bars _bars;
        private readonly AverageTrueRange _atr;

        // ===== NAS OPTIMIZED PARAMETERS =====

        // Teljes gyertya range mikortól számít extrémnek
        private const double ExtremeRangeAtrMult = 1.4;

        // Wick akkor gyanús, ha a body-hoz képest túl nagy
        private const double WickToBodyRatio = 1.2;

        // NAS: nagyon rövid pihenő
        private const int CooldownBars = 2;

        public NasImpulseGate(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;

            _atr = bot.Indicators.AverageTrueRange(
                _bars,
                14,
                MovingAverageType.Simple
            );
        }

        public bool AllowEntry(TradeType entryDirection)
        {
            int i = _bars.Count - 1;
            if (i < 2)
                return true;

            if (IsExtremeSpike(i, entryDirection))
            {
                _bot.Print("[NAS GATE] BLOCK extreme spike");
                return false;
            }

            if (IsInCooldown(i))
            {
                _bot.Print("[NAS GATE] BLOCK impulse cooldown");
                return false;
            }

            return true;
        }

        // =====================================================
        // EXTREME SPIKE DETECTION
        // (range + wick + direction)
        // =====================================================
        private bool IsExtremeSpike(int i, TradeType entryDirection)
        {
            double atr = _atr.Result[i];
            if (atr <= 0)
                return false;

            double high = _bars.HighPrices[i];
            double low = _bars.LowPrices[i];
            double open = _bars.OpenPrices[i];
            double close = _bars.ClosePrices[i];

            double range = high - low;
            if (range <= 0)
                return false;

            // 1️⃣ extrém teljes range
            bool extremeRange = range > atr * ExtremeRangeAtrMult;
            if (!extremeRange)
                return false;

            // 2️⃣ wick dominance
            double body = Math.Abs(close - open);
            if (body <= 0)
                return false;

            double upperWick = high - Math.Max(open, close);
            double lowerWick = Math.Min(open, close) - low;

            bool wickSpike =
                upperWick > body * WickToBodyRatio ||
                lowerWick > body * WickToBodyRatio;

            if (!wickSpike)
                return false;

            // 3️⃣ irány ellenőrzés (fake spike védelem)
            TradeType impulseDirection =
                close > open ? TradeType.Buy : TradeType.Sell;

            if (impulseDirection != entryDirection)
            {
                _bot.Print("[NAS GATE] spike direction mismatch");
                return true;
            }

            return false;
        }

        // =====================================================
        // COOLDOWN (rövid!)
        // =====================================================
        private bool IsInCooldown(int i)
        {
            for (int b = 1; b <= CooldownBars; b++)
            {
                int idx = i - b;
                if (idx < 0)
                    break;

                if (WasExtremeRange(idx))
                    return true;
            }
            return false;
        }

        private bool WasExtremeRange(int i)
        {
            double atr = _atr.Result[i];
            if (atr <= 0)
                return false;

            double range =
                _bars.HighPrices[i] - _bars.LowPrices[i];

            return range > atr * ExtremeRangeAtrMult;
        }
    }
}
