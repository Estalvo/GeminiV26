using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.METAL
{
    /// <summary>
    /// METAL Market State Detector
    /// XAU / XAG – instrument-szintű, ENTRY-TYPES független
    /// </summary>
    public sealed class MetalMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _bars;
        private readonly AverageTrueRange _atr;
        private readonly AverageDirectionalMovementIndexRating _adx;
        private readonly ExponentialMovingAverage _ema8;
        private readonly ExponentialMovingAverage _ema21;

        // Instrument-szintű threshold (nem EntryTypes!)
        private readonly double _minAtrPips;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;
        private const int EMA_FAST_PERIOD = 8;
        private const int EMA_SLOW_PERIOD = 21;

        /// <param name="bot">cTrader Robot instance</param>
        /// <param name="minAtrPips">
        /// Alacsony vol küszöb pipben.
        /// Ezt a hívó (router / instrument setup) adja meg.
        /// </param>
        public MetalMarketStateDetector(Robot bot, double minAtrPips)
        {
            _bot = bot;
            _bars = bot.Bars;
            _minAtrPips = minAtrPips;

            _atr = bot.Indicators.AverageTrueRange(
                _bars,
                ATR_PERIOD,
                MovingAverageType.Exponential);

            _adx = bot.Indicators.AverageDirectionalMovementIndexRating(
                _bars,
                ADX_PERIOD);

            _ema8 = bot.Indicators.ExponentialMovingAverage(
                _bars.ClosePrices,
                EMA_FAST_PERIOD);

            _ema21 = bot.Indicators.ExponentialMovingAverage(
                _bars.ClosePrices,
                EMA_SLOW_PERIOD);
        }

        public MetalMarketState Evaluate()
        {
            int i = _bars.ClosePrices.Count - 1;
            if (i < ATR_PERIOD)
                return null;

            double atrRaw = _atr.Result[i];
            double atrPips = atrRaw / _bot.Symbol.PipSize;

            double adx = _adx.ADX[i];
            double emaDist = System.Math.Abs(_ema8.Result[i] - _ema21.Result[i]);
            double emaDistATR = atrRaw > 0 ? emaDist / atrRaw : 0;

            bool lowVol = atrPips < _minAtrPips;
            bool trend = adx > 30;
            bool compression = emaDistATR < 0.5;
            bool hardRange = compression && adx < 25;

            if (adx > 40)
                hardRange = false;

            _bot.Print(
                "[XAU MSD] atrPips={0:F1} adx={1:F1} emaDistATR={2:F2} lowVol={3} trend={4} compression={5} hardRange={6}",
                atrPips,
                adx,
                emaDistATR,
                lowVol,
                trend,
                compression,
                hardRange);

            var state = new MetalMarketState
            {
                AtrPips = atrPips,
                IsRange = lowVol && !trend
            };

            TrySetStateValue(state, "Adx", adx);
            TrySetStateValue(state, "EmaDistanceAtr", emaDistATR);
            TrySetStateValue(state, "IsTrend", trend);
            TrySetStateValue(state, "IsLowVol", lowVol);
            TrySetStateValue(state, "IsCompression", compression);
            TrySetStateValue(state, "IsHardRange", hardRange);

            return state;
        }

        private static void TrySetStateValue(MetalMarketState state, string propertyName, object value)
        {
            var property = typeof(MetalMarketState).GetProperty(propertyName);
            if (property?.CanWrite == true)
                property.SetValue(state, value);
        }
    }
}
