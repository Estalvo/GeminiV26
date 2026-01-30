using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.EntryTypes.Crypto;

namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _m5;

        private readonly AverageTrueRange _atr;
        private readonly DirectionalMovementSystem _dms;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;

        private readonly CryptoInstrumentProfile _profile;

        public EthUsdMarketStateDetector(Robot bot)
        {
            _bot = bot;
            _m5 = bot.Bars;

            _atr = bot.Indicators.AverageTrueRange(_m5, ATR_PERIOD, MovingAverageType.Exponential);
            _dms = bot.Indicators.DirectionalMovementSystem(_m5, ADX_PERIOD);

            // crypto matrix -> paraméterek
            _profile = CryptoInstrumentMatrix.Get("ETHUSD");
        }

        public EthUsdMarketState Evaluate()
        {
            int i = _m5.ClosePrices.Count - 1;
            if (i < Math.Max(ATR_PERIOD, ADX_PERIOD))
                return null;

            double atrRaw = _atr.Result[i];
            double atrPips = atrRaw / _bot.Symbol.PipSize;

            double adx = _dms.ADX[i];

            // wick ratio (current bar)
            double high = _m5.HighPrices[i];
            double low = _m5.LowPrices[i];
            double open = _m5.OpenPrices[i];
            double close = _m5.ClosePrices[i];

            double range = high - low;
            double body = Math.Abs(close - open);
            double wick = Math.Max(0, range - body);
            double wickRatio = range > 0 ? wick / range : 0;

            bool isLowVol = atrPips < _profile.MinAtrPips;
            bool isExtremeVol = _profile.MaxAtrPips > 0 && atrPips > _profile.MaxAtrPips;

            bool isTrend = adx >= _profile.MinAdxTrend;
            bool isStrongTrend = adx >= _profile.MinAdxStrong;

            // chop: több bar-on túl wicky
            bool isChop = false;
            int lb = Math.Max(1, _profile.ChopLookbackBars);
            int start = Math.Max(0, i - lb + 1);
            int wicky = 0;

            for (int k = start; k <= i; k++)
            {
                double h = _m5.HighPrices[k];
                double l = _m5.LowPrices[k];
                double o = _m5.OpenPrices[k];
                double c = _m5.ClosePrices[k];

                double r = h - l;
                if (r <= 0) continue;

                double b = Math.Abs(c - o);
                double w = Math.Max(0, r - b);
                double wr = w / r;

                if (wr >= _profile.MaxWickRatio)
                    wicky++;
            }
            isChop = wicky >= lb;

            _bot.Print(
                $"[ETH MSD] atrRaw={atrRaw:F2} pipSize={_bot.Symbol.PipSize} atrPips={atrPips:F1} adx={adx:F1} wickRatio={wickRatio:F2} " +
                $"lowVol={isLowVol} extremeVol={isExtremeVol} trend={isTrend} strongTrend={isStrongTrend} chop={isChop}"
            );

            return new EthUsdMarketState
            {
                AtrPips = atrPips,
                Adx = adx,
                WickRatioNow = wickRatio,

                IsLowVol = isLowVol,
                IsExtremeVol = isExtremeVol,

                IsTrend = isTrend,
                IsStrongTrend = isStrongTrend,

                IsChop = isChop
            };
        }
    }
}
