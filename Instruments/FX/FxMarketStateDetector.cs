using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.EntryTypes.FX;

namespace GeminiV26.Instruments.FX
{
    /// <summary>
    /// FX Market State Detector
    ///
    /// - FX-szintű, közös implementáció
    /// - instrument viselkedést a FxInstrumentProfile határozza meg
    /// - nincs hardcoded symbol-logika
    /// </summary>
    public sealed class FxMarketStateDetector
    {
        private readonly Robot _bot;
        private readonly Bars _bars;

        private readonly AverageTrueRange _atr;
        private readonly DirectionalMovementSystem _adx;

        private readonly FxInstrumentProfile _profile;

        private const int ATR_PERIOD = 14;
        private const int ADX_PERIOD = 14;

        public FxMarketStateDetector(Robot bot, string symbol)
        {
            _bot = bot;
            _bars = bot.Bars;

            _bot.Print($"[FX MATRIX DBG] MatrixType = {typeof(FxInstrumentMatrix).Assembly.FullName}");
            _bot.Print($"[FX MATRIX DBG] Keys = {string.Join(",", FxInstrumentMatrix.DebugKeys())}");

            _atr = bot.Indicators.AverageTrueRange(
                _bars,
                ATR_PERIOD,
                MovingAverageType.Exponential);

            _adx = bot.Indicators.DirectionalMovementSystem(
                _bars,
                ADX_PERIOD);

            string raw = symbol ?? string.Empty;
            string key = NormalizeFxSymbol(raw);

            if (!FxInstrumentMatrix.Contains(key))
            {
                throw new InvalidOperationException(
                    $"FxMarketStateDetector used on non-FX symbol: raw={raw} norm={key}");
            }

            _profile = FxInstrumentMatrix.Get(key);
        }

        private static string NormalizeFxSymbol(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return string.Empty;

            string s = symbol.Trim().ToUpperInvariant();

            // vágás első elválasztónál: USDCHF.i / USDCHF-ECN / USDCHF_m / "USDCHF m"
            int cut = s.IndexOfAny(new[] { '.', '-', '_', ' ' });
            if (cut > 0)
                s = s.Substring(0, cut);

            return s;
        }

        /// <summary>
        /// Aktuális FX környezet kiértékelése
        /// </summary>
        public FxMarketState Evaluate()
        {
            int i = _bars.ClosePrices.Count - 1;
            if (i < Math.Max(ATR_PERIOD, ADX_PERIOD))
                return null;

            double atrRaw = _atr.Result[i];
            double atrPips = atrRaw / _bot.Symbol.PipSize;
            double adx = _adx.ADX[i];

            // === PROFIL VEZÉRELT ÉRTELMEZÉS ===
            bool isLowVol = atrPips < _profile.MinAtrPips;
            bool isTrend = adx >= _profile.MinAdxTrend;

            // === DEBUG (szándékosan marad) ===
            _bot.Print(
                $"[FX MSD] {_bot.SymbolName} | " +
                $"atrPips={atrPips:F2} adx={adx:F1} | " +
                $"lowVol={isLowVol} trend={isTrend}");

            return new FxMarketState
            {
                AtrPips = atrPips,
                Adx = adx,
                IsLowVol = isLowVol,
                IsTrend = isTrend
            };
        }
    }
}
