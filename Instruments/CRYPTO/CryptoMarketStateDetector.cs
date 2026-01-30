using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.CRYPTO
{
    /// <summary>
    /// CRYPTO Market State Detector
    /// BTC / crypto instrumentumokra
    /// </summary>
    public sealed class CryptoMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        private readonly AverageTrueRange _atr;
        private readonly DirectionalMovementSystem _dms;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;

        public CryptoMarketStateDetector(Robot bot)
        {
            _bot = bot;
            _bars = bot.Bars;

            _atr = bot.Indicators.AverageTrueRange(
                _bars,
                ATR_PERIOD,
                MovingAverageType.Exponential);

            _dms = bot.Indicators.DirectionalMovementSystem(
                _bars,
                ADX_PERIOD);
        }

        public CryptoMarketState Evaluate()
        {
            int i = _bars.ClosePrices.Count - 1;
            if (i < Math.Max(ATR_PERIOD, ADX_PERIOD))
                return null;

            double atr = _atr.Result[i];
            double adx = _dms.ADX[i];

            bool isHighVol = atr > 100;
            bool isLowVol = atr < 40;
            bool isTrend = adx >= 20;

            _bot.Print(
                $"[CRYPTO MSD] {_bot.SymbolName} | " +
                $"ATR={atr:F1} ADX={adx:F1} " +
                $"HighVol={isHighVol} LowVol={isLowVol} Trend={isTrend}");

            return new CryptoMarketState
            {
                AtrUsd = atr,
                AtrPips = atr,
                Adx = adx,
                IsHighVol = isHighVol,
                IsLowVol = isLowVol,
                IsTrend = isTrend
            };
        }
    }
}
