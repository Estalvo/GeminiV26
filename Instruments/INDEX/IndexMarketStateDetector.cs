using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.INDEX
{
    /// <summary>
    /// INDEX Market State Detector
    ///
    /// - index-szintű, közös implementáció
    /// - jelenleg egységes logika (NAS / US30 / GER40)
    /// - NINCS hard tiltás symbolra
    /// </summary>
    public sealed class IndexMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        private readonly AverageTrueRange _atr;
        private readonly DirectionalMovementSystem _adx;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;

        // ===== PROFILE (KIEGÉSZÍTÉS) =====
        private readonly IndexInstrumentProfile _profile;

        public double Adx { get; set; }

        public IndexMarketStateDetector(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;

            // ===== PROFILE LOAD (SAFE) =====
            if (IndexInstrumentMatrix.Contains(bot.SymbolName))
                _profile = IndexInstrumentMatrix.Get(bot.SymbolName);
            // else: fallback → null profile, hardcoded értékekkel fut

            _atr = bot.Indicators.AverageTrueRange(
                _bars,
                ATR_PERIOD,
                MovingAverageType.Exponential);

            _adx = bot.Indicators.DirectionalMovementSystem(
                _bars,
                ADX_PERIOD);
        }

        public IndexMarketState Evaluate()
        {
            int i = _bars.ClosePrices.Count - 1;
            if (i < Math.Max(ATR_PERIOD, ADX_PERIOD))
                return null;

            double atrRaw = _atr.Result[i];
            double atrPoints = atrRaw / _bot.Symbol.TickSize;
            double adx = _adx.ADX[i];

            // =====================================================
            // PROFILE-DRIVEN THRESHOLDS (FALLBACK SAFE)
            // =====================================================
            double minAtrPoints =
                _profile != null && _profile.MinAtrPoints > 0
                    ? _profile.MinAtrPoints
                    : 20;   // fallback (eredeti érték)

            double minAdxTrend =
                _profile != null && _profile.MinAdxTrend > 0
                    ? _profile.MinAdxTrend
                    : 14;   // fallback (eredeti érték)
            // =====================================================

            bool isLowVol = atrPoints < minAtrPoints;
            bool isTrend = adx >= minAdxTrend;

            _bot.Print(
                $"[INDEX MSD] {_bot.SymbolName} | " +
                $"atrPts={atrPoints:F1} adx={adx:F1} | " +
                $"lowVol={isLowVol} trend={isTrend} | " +
                $"minAtr={minAtrPoints} minAdx={minAdxTrend}");

            return new IndexMarketState
            {
                AtrPoints = atrPoints,
                Adx = adx,
                IsLowVol = isLowVol,
                IsTrend = isTrend
            };
        }
    }
}
