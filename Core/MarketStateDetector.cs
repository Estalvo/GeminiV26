using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Core
{
    // =====================================================
    // MARKET STATE DETECTOR (XAU ACTIVE)
    // =====================================================
    public class MarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        private readonly DirectionalMovementSystem _dms;
        private readonly AverageTrueRange _atr;

        // =========================
        // RANGE MEMORY
        // =========================
        private double _rangeHigh;
        private double _rangeLow;
        private int _rangeBarCount;

        // =========================
        // CONFIG
        // =========================
        private const int LookbackBars = 30;
        private const int VolumeSmaPeriod = 20;

        // XAU parameters
        private const double XauRangeMax = 17.0;
        private const double XauSoftRangeMax = 14.0;

        private const double XauAdxRangeMax = 18.0;
        private const double XauAdxTrendMin = 20.0;

        private const double XauVolumeRangeMax = 1.2;
        private const double XauVolumeBreakoutMin = 1.5;

        private const int PostBreakoutBars = 3;
        private int _postBreakoutCountdown = 0;

        public MarketStateDetector(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;

            _dms = bot.Indicators.DirectionalMovementSystem(14);
            _atr = bot.Indicators.AverageTrueRange(14, MovingAverageType.Simple);
        }

        // =====================================================
        // PUBLIC API
        // =====================================================
        public MarketState Evaluate()
        {
            var state = new MarketState();

            if (_bars.Count < LookbackBars + 2)
                return state;

            // ---------------------------------
            // RANGE WIDTH
            // ---------------------------------
            double high = double.MinValue;
            double low = double.MaxValue;

            for (int i = 1; i <= LookbackBars; i++)
            {
                high = Math.Max(high, _bars.HighPrices.Last(i));
                low = Math.Min(low, _bars.LowPrices.Last(i));
            }

            double rangeWidth = high - low;
            double adx = _dms.ADX.LastValue;
            double atr = _atr.Result.LastValue;

            // ---------------------------------
            // VOLUME NORMALIZATION
            // ---------------------------------
            double volNow = _bars.TickVolumes.LastValue;
            double volSum = 0;

            for (int i = 1; i <= VolumeSmaPeriod; i++)
                volSum += _bars.TickVolumes.Last(i);

            double volSma = volSum / VolumeSmaPeriod;
            double volNorm = volSma > 0 ? volNow / volSma : 1.0;

            // ---------------------------------
            // ASSIGN RAW VALUES
            // ---------------------------------
            state.RangeWidth = rangeWidth;
            state.Adx = adx;
            state.Atr = atr;
            state.VolumeNorm = volNorm;

            // ---------------------------------
            // ONLY XAU ACTIVE
            // ---------------------------------
            if (!_bot.SymbolName.Contains("XAU"))
                return state;

            // ---------------------------------
            // RANGE STATES
            // ---------------------------------
            bool isSoftRange =
                rangeWidth <= XauSoftRangeMax &&
                adx < XauAdxRangeMax;

            bool isRange =
                rangeWidth <= XauRangeMax &&
                adx < XauAdxRangeMax &&
                volNorm <= XauVolumeRangeMax;

            bool rangeEnding =
                rangeWidth > XauRangeMax ||
                adx >= XauAdxTrendMin ||
                volNorm >= XauVolumeBreakoutMin;

            // ---------------------------------
            // BREAKOUT
            // ---------------------------------
            bool breakout = false;

            if (rangeEnding && _rangeBarCount >= LookbackBars / 2)
            {
                bool close1 =
                    _bars.ClosePrices.Last(1) > _rangeHigh ||
                    _bars.ClosePrices.Last(1) < _rangeLow;

                bool close2 =
                    _bars.ClosePrices.Last(2) > _rangeHigh ||
                    _bars.ClosePrices.Last(2) < _rangeLow;

                if (close1 && close2 &&
                    adx >= XauAdxTrendMin &&
                    volNorm >= XauVolumeBreakoutMin)
                {
                    breakout = true;
                    _postBreakoutCountdown = PostBreakoutBars;
                }
            }

            // ---------------------------------
            // RANGE MEMORY
            // ---------------------------------
            if (isRange)
            {
                _rangeHigh = high;
                _rangeLow = low;
                _rangeBarCount++;
            }
            else
            {
                _rangeBarCount = 0;
            }

            // ---------------------------------
            // POST BREAKOUT
            // ---------------------------------
            bool postBreakout = false;
            if (_postBreakoutCountdown > 0)
            {
                postBreakout = true;
                _postBreakoutCountdown--;
            }

            if (_bot.SymbolName.Contains("XAU"))
            {
                state.IsBreakout = true; // DEBUG
            }

            // ---------------------------------
            // FINAL FLAGS
            // ---------------------------------
            state.IsSoftRange = isSoftRange;
            state.IsRange = isRange;
            state.IsRangeEnding = rangeEnding;
            state.IsBreakout = breakout;
            state.IsPostBreakout = postBreakout;

            double atrPipsDbg = state.Atr / _bot.Symbol.PipSize;
            double widthPipsDbg = state.RangeWidth / _bot.Symbol.PipSize;

            _bot.Print($"[MarketStateDBG] {_bot.SymbolName} atrRaw={state.Atr:F6} atrPips={atrPipsDbg:F2} widthRaw={state.RangeWidth:F6} widthPips={widthPipsDbg:F1}");

            return state;
        }
    }

    // =====================================================
    // DATA MODEL
    // =====================================================
    public class MarketState
    {
        public bool IsSoftRange { get; set; }
        public bool IsRange { get; set; }
        public bool IsRangeEnding { get; set; }
        public bool IsBreakout { get; set; }
        public bool IsPostBreakout { get; set; }

        public double RangeWidth { get; set; }
        public double Adx { get; set; }
        public double Atr { get; set; }
        public double VolumeNorm { get; set; }
    }
}
