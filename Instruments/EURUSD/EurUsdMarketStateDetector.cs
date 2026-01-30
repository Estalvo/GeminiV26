using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Instruments.FX;   // <-- FX profile
using System;

namespace GeminiV26.Instruments.EURUSD
{
    public class EurUsdMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _m5;

        private readonly AverageTrueRange _atr;
        private readonly DirectionalMovementSystem _adx;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;

        // FX profil (EURUSD)
        private readonly FxInstrumentProfile _fxProfile;

        public EurUsdMarketStateDetector(Robot bot)
        {
            _bot = bot;
            _m5 = bot.Bars;

            _atr = bot.Indicators.AverageTrueRange(
                _m5, ATR_PERIOD, MovingAverageType.Exponential);

            _adx = bot.Indicators.DirectionalMovementSystem(
                _m5, ADX_PERIOD);

            // ===== FX MATRIXBÓL PROFIL BETÖLTÉSE =====
            // NINCS döntés, csak paraméter
            _fxProfile = FxInstrumentMatrix.Get("EURUSD");
        }

        public EurUsdMarketState Evaluate()
        {
            int i = _m5.ClosePrices.Count - 1;
            if (i < Math.Max(ATR_PERIOD, ADX_PERIOD))
                return null;

            double atrRaw = _atr.Result[i];
            double atrPips = atrRaw / _bot.Symbol.PipSize;
            double adx = _adx.ADX[i];

            // DEBUG – ez marad, nagyon hasznos
            _bot.Print(
                $"[EUR MSD] atrRaw={atrRaw:F6} pipSize={_bot.Symbol.PipSize} atrPips={atrPips:F2} adx={adx:F1}");

            // ===== FX-PROFIL VEZÉRELT KÜSZÖBÖK =====

            // Alacsony volatilitás: FX-mátrix szerint
            bool isLowVol = atrPips < _fxProfile.MinAtrPips;

            // Trend érvényesség (nem entry, csak state!)
            bool isTrend = adx >= _fxProfile.MinAdxTrend;

            return new EurUsdMarketState
            {
                AtrPips = atrPips,
                Adx = adx,

                // állapotjelzők (NEM belépési logika)
                IsLowVol = isLowVol,
                IsTrend = isTrend
            };
        }
    }
}
