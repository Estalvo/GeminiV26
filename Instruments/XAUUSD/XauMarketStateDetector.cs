using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.EntryTypes.METAL;

namespace GeminiV26.Instruments.XAUUSD
{
    public sealed class XauMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _m5;

        private readonly AverageTrueRange _atr;
        private readonly DirectionalMovementSystem _dms;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;

        private readonly XAU_InstrumentProfile _p;

        public XauMarketStateDetector(Robot bot)
        {
            _bot = bot;
            _m5 = bot.Bars;

            _atr = bot.Indicators.AverageTrueRange(_m5, ATR_PERIOD, MovingAverageType.Exponential);
            _dms = bot.Indicators.DirectionalMovementSystem(_m5, ADX_PERIOD);

            _p = XAU_InstrumentMatrix.Get(bot.SymbolName);
        }

        public XauMarketState Evaluate()
        {
            int i = _m5.ClosePrices.Count - 1;
            
            bool hasEnoughData =
                i >= Math.Max(ATR_PERIOD, ADX_PERIOD) + _p.RangeLookbackBars;

            double atrRaw = _atr.Result[i];
            double atrPips = atrRaw / _bot.Symbol.PipSize;
            double adx = _dms.ADX[i];

            // Wick ratio (current bar)
            double high = _m5.HighPrices[i];
            double low = _m5.LowPrices[i];
            double open = _m5.OpenPrices[i];
            double close = _m5.ClosePrices[i];

            double range = high - low;
            double body = Math.Abs(close - open);
            double wick = Math.Max(0, range - body);
            double wickRatio = range > 0 ? wick / range : 0;

            // Range width ATR (lookback)
            int lb = Math.Max(5, _p.RangeLookbackBars);
            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int k = i - lb + 1; k <= i; k++)
            {
                hi = Math.Max(hi, _m5.HighPrices[k]);
                lo = Math.Min(lo, _m5.LowPrices[k]);
            }

            double width = hi - lo;
            double widthAtr = atrRaw > 0 ? width / atrRaw : 999;

            bool isLowVol = atrPips < _p.MinAtrPips;
            bool isTrend = adx >= _p.MinAdxTrend;

            bool isHardRange = widthAtr <= _p.RangeMaxWidthAtr && !isTrend; // szűk + nem trend
            bool isChop = wickRatio >= _p.MaxWickRatio;

            _bot.Print(
                $"[XAU MSD] atrPips={atrPips:F1} adx={adx:F1} widthATR={widthAtr:F2} " +
                $"lowVol={isLowVol} trend={isTrend} hardRange={isHardRange} wickRatio={wickRatio:F2} chop={isChop}"
            );
            
            return new XauMarketState
            {
                AtrPips = atrPips,
                Adx = adx,

                IsLowVol = hasEnoughData ? isLowVol : true,
                IsTrend = hasEnoughData ? isTrend : false,
                RangeWidthAtr = widthAtr,
                IsHardRange = hasEnoughData ? isHardRange : false,

                WickRatioNow = wickRatio,
                IsChop = hasEnoughData ? isChop : true
            };
        }
    }
}
