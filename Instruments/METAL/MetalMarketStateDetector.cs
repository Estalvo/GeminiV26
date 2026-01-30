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

        // Instrument-szintű threshold (nem EntryTypes!)
        private readonly double _minAtrPips;

        private const int ATR_PERIOD = 14;

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
        }

        public MetalMarketState Evaluate()
        {
            int i = _bars.ClosePrices.Count - 1;
            if (i < ATR_PERIOD)
                return null;

            double atrRaw = _atr.Result[i];
            double atrPips = atrRaw / _bot.Symbol.PipSize;

            bool isLowVol = atrPips < _minAtrPips;

            return new MetalMarketState
            {
                AtrPips = atrPips,
                IsRange = isLowVol
            };
        }
    }
}
