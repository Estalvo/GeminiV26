// =========================================================
// GEMINI V26 – EURUSD EntryLogic
// Phase 3.7.x – RULEBOOK 1.0 COMPLIANT
//
// SZEREP:
// - AUDNZD instrument-specifikus belépési LOGIKA
// - Trend/bias meghatározás (Buy / Sell)
// - LogicConfidence (0–100) számítása
//
// RULEBOOK 1.0 (Phase 3.7) ALAPELVEK:
// - EntryLogic NEM gate (nem tilt, nem vétóz)
// - EntryLogic NEM dönt trade indításról
// - EntryLogic NEM használ hard return false-okat "setup" okból
// - Impulse / Session ellenőrzés NEM itt van (Gate-ek a TradeCore-ban)
//
// KIMENET:
// - LastBias (TradeType.Buy / TradeType.Sell)
// - LastLogicConfidence (0–100)
// =========================================================

using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Instruments.FX;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.AUDNZD
{
    public class AudNzdEntryLogic : IEntryLogic
    {
        private readonly Robot _bot;

        // TF-ek
        private readonly Bars _m5;
        private readonly Bars _m15;

        // M5 trend
        private readonly ExponentialMovingAverage _ema50;
        private readonly ExponentialMovingAverage _ema200;

        // HTF sanity (nem gate!)
        private readonly ExponentialMovingAverage _emaHtf;

        // Strength / volatility (soft scoring)
        private readonly AverageDirectionalMovementIndexRating _adx;
        private readonly AverageTrueRange _atr;

        // Baseline paraméterek
        private const int MinBars = 250;
        private const int EmaFast = 50;
        private const int EmaSlow = 200;

        private const int AdxPeriod = 14;
        private const double AdxTrend = 16.0;   
        private const double AdxStrong = 23.0;  

        private const int AtrPeriod = 14;

        // Output
        public int LastLogicConfidence { get; private set; }
        public TradeType LastBias { get; private set; }

        public AudNzdEntryLogic(Robot bot)
        {
            _bot = bot;

            _m5 = bot.MarketData.GetBars(TimeFrame.Minute5);
            _m15 = bot.MarketData.GetBars(TimeFrame.Minute15);

            // Indicators (M5)
            _ema50 = bot.Indicators.ExponentialMovingAverage(_m5.ClosePrices, EmaFast);
            _ema200 = bot.Indicators.ExponentialMovingAverage(_m5.ClosePrices, EmaSlow);

            // HTF baseline (M15 EMA21)
            _emaHtf = bot.Indicators.ExponentialMovingAverage(_m15.ClosePrices, 21);

            // ADX / ATR
            _adx = bot.Indicators.AverageDirectionalMovementIndexRating(AdxPeriod);
            _atr = bot.Indicators.AverageTrueRange(_m5, AtrPeriod, MovingAverageType.Exponential);
        }

        /// <summary>
        /// 3.7-es szerződés:
        /// - mindig beállítja a LastBias + LastLogicConfidence értékeket
        /// - nem vétóz (nincs setup-alapú return false)
        /// </summary>
        public void Evaluate()
        {
            LastBias = LastBias == 0 ? TradeType.Buy : LastBias;
            LastLogicConfidence = 50;

            if (_m5 == null || _m5.Count < MinBars || _m15 == null || _m15.Count < 50)
            {
                GlobalLogger.Log($"[AUDNZD LOGIC] bars insufficient (m5={_m5?.Count ?? 0}, m15={_m15?.Count ?? 0}) -> default bias/conf");
                return;
            }

            var result = FxBiasTuningHelper.Evaluate(
                _m5,
                _m15,
                _ema50.Result,
                _ema200.Result,
                _emaHtf.Result,
                _adx,
                _atr);

            LastBias = result.Bias;
            LastLogicConfidence = result.Confidence;

            GlobalLogger.Log($"[AUDNZD TRACE] step1_logic={LastBias} conf={LastLogicConfidence}");

            if (result.State == "FX_FALLBACK")
                GlobalLogger.Log("[FX BIAS FALLBACK] trend-based bias");

            GlobalLogger.Log($"[AUDNZD LOGIC] state={result.State} bias={LastBias} logicConf={LastLogicConfidence} | {result.Details}");
        }
    }
}
