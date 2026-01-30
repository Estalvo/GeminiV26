// =========================================================
// GEMINI V26 – NZDUSD EntryLogic
// Phase 3.7.x – RULEBOOK 1.0 COMPLIANT
//
// SZEREP:
// - NzdUsd instrument-specifikus belépési LOGIKA
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
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.NZDUSD
{
    public class NzdUsdEntryLogic : IEntryLogic
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

        public NzdUsdEntryLogic(Robot bot)
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
            // Safe defaults (router majd dönt)
            LastBias = TradeType.Buy;
            LastLogicConfidence = 50;

            if (_m5 == null || _m5.Count < MinBars || _m15 == null || _m15.Count < 50)
            {
                _bot.Print($"[NZDUSD LOGIC] bars insufficient (m5={_m5?.Count ?? 0}, m15={_m15?.Count ?? 0}) -> default bias/conf");
                return;
            }

            int i = _m5.Count - 1;

            // ===== Trend direction (M5) =====
            double ema50 = _ema50.Result[i];
            double ema200 = _ema200.Result[i];
            double emaDiff = ema50 - ema200;

            // Bias (nincs veto, csak default+soft)
            if (emaDiff > 0)
                LastBias = TradeType.Buy;
            else if (emaDiff < 0)
                LastBias = TradeType.Sell;

            // ===== Strength / volatility =====
            double adx = _adx.ADX.LastValue;
            double atr = _atr.Result.LastValue;

            // ===== HTF sanity =====
            double htfEma = _emaHtf.Result.LastValue;
            double htfPrice = _m15.ClosePrices.LastValue;
            bool htfBull = htfPrice >= htfEma;

            // =====================================================
            // LOGIC CONFIDENCE (SOFT SCORING)
            // =====================================================
            int conf = 50;

            // Trend exists (EMA50 vs EMA200)
            if (Math.Abs(emaDiff) > 0)
                conf += 10;

            // ADX trend strength (soft)
            if (adx >= AdxTrend) conf += 10;
            if (adx >= AdxStrong) conf += 10;

            // EMA separation relative to ATR (soft)
            double emaAbs = Math.Abs(emaDiff);
            if (atr > 0 && emaAbs > atr * 0.35) // EUR: enyhébb, mint NAS
                conf += 10;

            // HTF alignment bonus (soft)
            if (LastBias == TradeType.Buy && htfBull) conf += 5;
            else if (LastBias == TradeType.Sell && !htfBull) conf += 5;

            // Soft “uncertain” penalty, ha EMA-k túl közel vannak egymáshoz
            if (atr > 0 && emaAbs < atr * 0.12)
                conf -= 5;

            // Clamp 0..100
            conf = Math.Max(0, Math.Min(100, conf));
            LastLogicConfidence = conf;

            // =====================================================
            // DEBUG (informatív, nem gate)
            // =====================================================
            _bot.Print(
                $"[NZDUSD LOGIC] bias={LastBias} logicConf={LastLogicConfidence} | " +
                $"ema50={ema50:F5} ema200={ema200:F5} diff={emaDiff:F5} | " +
                $"adx={adx:F1} atr={atr:F5} | htfBull={htfBull}"
            );
        }
    }
}
