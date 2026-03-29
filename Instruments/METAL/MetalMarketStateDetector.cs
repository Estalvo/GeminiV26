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

            _adx = bot.Indicators.AverageDirectionalMovementIndexRating(ADX_PERIOD);

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
            double high = _bars.HighPrices[i];
            double low = _bars.LowPrices[i];
            double body = System.Math.Abs(_bars.ClosePrices[i] - _bars.OpenPrices[i]);

            double upperWick = high - System.Math.Max(_bars.ClosePrices[i], _bars.OpenPrices[i]);
            double lowerWick = System.Math.Min(_bars.ClosePrices[i], _bars.OpenPrices[i]) - low;
            double emaDistATR = atrRaw > 0 ? emaDist / atrRaw : 0;

            bool lowVol = atrPips < _minAtrPips;
            bool trend = adx > 30;
            bool compression = emaDistATR < 0.5;
            bool momentum = atrRaw > 0 && body >= atrRaw * 0.7 && !compression;
            bool hardRange = compression && adx < 25;
            bool isSpike = upperWick > body * 2 || lowerWick > body * 2;

            if (adx > 40)
                hardRange = false;

            GlobalLogger.Log(
                "[XAU MSD] atrPips={0:F1} adx={1:F1} emaDistATR={2:F2} lowVol={3} trend={4} momentum={5} compression={6} hardRange={7} spike={8}",
                atrPips,
                adx,
                emaDistATR,
                lowVol,
                trend,
                momentum,
                compression,
                hardRange,
                isSpike);

            return new MetalMarketState
            {
                AtrPips = atrPips,
                Adx = adx,
                IsLowVol = lowVol,
                IsTrend = trend,
                IsMomentum = momentum,
                IsCompression = compression,
                IsHardRange = hardRange,
                IsRange = lowVol && !trend,
                EmaDistanceAtr = emaDistATR,
                IsSpike = isSpike
            };
        }
    }
}
