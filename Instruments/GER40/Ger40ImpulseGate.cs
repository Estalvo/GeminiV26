using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.GER40
{
    /// <summary>
    /// GER40 Impulse Gate (SOFT)
    /// --------------------------
    /// Cél:
    /// - extrém spike-ok szűrése
    /// - manipulációs wickek kizárása
    /// - normál GER40 impulzusok ENGEDÉSE
    ///
    /// FONTOS:
    /// - NINCS body-only tilt
    /// - csak EXTREME + WICK együtt blokkol
    /// - rövid cooldown
    /// </summary>
    public class Ger40ImpulseGate : IGate
    {
        private readonly Robot _bot;
        private readonly Bars _bars;
        private readonly AverageTrueRange _atr;
        // utolsó ténylegesen BLOKKOLT spike bar indexe
        private int _lastBlockedBar = -1000;

        // ===== GER40 OPTIMIZED PARAMETERS =====

        // Teljes gyertya range mikortól számít extrémnek
        private const double ExtremeRangeAtrMult = 1.3;

        // Wick akkor gyanús, ha a body-hoz képest túl nagy
        private const double WickToBodyRatio = 1.30;

        // GER40: nagyon rövid pihenő
        private const int CooldownBars = 3;

        public Ger40ImpulseGate(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;

            _atr = bot.Indicators.AverageTrueRange(
                _bars,
                14,
                MovingAverageType.Exponential
            );
        }

        public bool AllowEntry(TradeType entryDirection)
        {
            int i = _bars.Count - 1;
            if (i < 2)
                return true;

            // === 1) VALÓDI SPIKE MIATTI BLOKK ===
            if (IsBlockingSpike(i, entryDirection))
            {
                _lastBlockedBar = i;
                _bot.Print("[GER40 GATE] BLOCK extreme spike");
                return false;
            }

            // === 2) COOLDOWN CSAK BLOKK UTÁN ===
            if (i - _lastBlockedBar <= CooldownBars)
            {
                _bot.Print("[GER40 GATE] BLOCK impulse cooldown");
                return false;
            }

            // === 3) SEMMI NEM TILT ===
            return true;
        }

        // =====================================================
        // EXTREME SPIKE DETECTION
        // (range + wick + direction)
        // =====================================================
        private bool IsBlockingSpike(int i, TradeType entryDirection)
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

            // 1) extrém teljes range
            if (range <= atr * ExtremeRangeAtrMult)
                return false;

            // 2) wick dominance
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

            // 3) irány mismatch = BLOKK
            TradeType impulseDirection =
                close > open ? TradeType.Buy : TradeType.Sell;

            return impulseDirection != entryDirection;
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
