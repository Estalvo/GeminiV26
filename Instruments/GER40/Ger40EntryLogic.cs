// =========================================================
// GEMINI V26 – GER40 EntryLogic
// Phase 3.7.x – RULEBOOK 1.0 COMPLIANT
//
// SZEREP:
// - GER40 instrument-specifikus belépési LOGIKA
// - Trend/bias meghatározás (Long / Short)
// - LogicConfidence (0–100) számítása
//
// FONTOS ALAPELVEK (Rulebook 1.0):
// - EntryLogic NEM belépési gate
// - EntryLogic NEM vétózhat
// - EntryLogic NEM dönt trade indításról
// - EntryLogic csak INFORMÁCIÓT szolgáltat
//
// TILOS:
// - hard threshold (confidence >= X) mint belépési feltétel
// - return false mint tiltás
// - impulse / session gate logika ide
//
// Gate-ek HELYE:
// - KIZÁRÓLAG TradeCore (SessionGate + ImpulseGate)
//
// KIMENET:
// - LastBias (TradeType.Buy / TradeType.Sell)
// - LastLogicConfidence (0–100)
// =========================================================

using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Interfaces;
using System;

namespace GeminiV26.Instruments.GER40
{
    public class Ger40EntryLogic : IEntryLogic
    {
        private readonly Robot _bot;

        // M5 = chart TF, M15 = HTF trend sanity (baseline)
        private readonly Bars _m5;
        private readonly Bars _m15;

        // Trend baseline (GER40)
        private readonly IndicatorDataSeries _ema50;
        private readonly IndicatorDataSeries _ema200;

        // HTF EMA (könnyű zajszűrés, nem gate!)
        private readonly IndicatorDataSeries _emaHtf;

        // Trend strength
        private readonly AverageDirectionalMovementIndexRating _adx;

        // Volatility scaling for confidence (NOT gate!)
        private readonly AverageTrueRange _atr;

        // ----- baseline params (Phase 3.7.x) -----
        private const int MinBars = 120;

        private const int EmaFast = 50;
        private const int EmaSlow = 200;

        private const int AdxPeriod = 14;
        private const double AdxTrend = 17.0;     // csak confidence pontozás
        private const double AdxStrong = 22.0;    // csak confidence pontozás

        private const int AtrPeriod = 14;

        // Output
        public int LastLogicConfidence { get; private set; }
        public TradeType LastBias { get; private set; }

        public Ger40EntryLogic(Robot bot)
        {
            _bot = bot;

            _m5 = bot.Bars;
            _m15 = bot.MarketData.GetBars(TimeFrame.Minute15);

            // Indicators (M5)
            _ema50 = bot.Indicators.ExponentialMovingAverage(_m5.ClosePrices, EmaFast).Result;
            _ema200 = bot.Indicators.ExponentialMovingAverage(_m5.ClosePrices, EmaSlow).Result;

            // HTF trend sanity (M15 EMA21 baseline)
            _emaHtf = bot.Indicators.ExponentialMovingAverage(_m15.ClosePrices, 21).Result;

            // ADX / ATR
            _adx = bot.Indicators.AverageDirectionalMovementIndexRating(AdxPeriod);
            _atr = bot.Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
        }

        /// <summary>
        /// EntryLogic értékelés.
        /// NEM tilt, NEM gate-el.
        /// Mindig beállítja: LastBias + LastLogicConfidence.
        /// </summary>
        public void Evaluate()
        {
            // Safe defaults
            LastBias = LastBias == 0 ? TradeType.Buy : LastBias;
            LastLogicConfidence = 50;

            if (_m5 == null || _m5.Count < MinBars)
            {
                _bot.Print($"[GER40 LOGIC] bars<{MinBars} (count={_m5?.Count ?? 0}) -> default bias/conf");
                return;
            }

            int i = _m5.Count - 1;

            // --- trend direction (M5) ---
            double ema50 = _ema50[i];
            double ema200 = _ema200[i];

            // If equal / too close, keep default bias but reduce confidence slightly
            double emaDiff = ema50 - ema200;

            // --- strength/volatility ---
            double adx = _adx.ADX.LastValue;
            double atr = _atr.Result.LastValue;

            // --- HTF sanity (optional, informational only) ---
            double htfEma = _emaHtf.LastValue;
            double priceM15 = _m15.ClosePrices.LastValue;
            bool htfBull = priceM15 >= htfEma;

            // =========================
            // SIGNAL + BIAS
            // =========================
            GerEntrySignal signal = GerEntrySignal.None;

            double deadzone = atr * 0.05;

            if (Math.Abs(emaDiff) < deadzone)
            {
                signal = GerEntrySignal.None;
                // bias szándékosan NEM változik
            }
            else if (emaDiff > 0)
            {
                signal = GerEntrySignal.LongTrend;
                LastBias = TradeType.Buy;
            }
            else
            {
                signal = GerEntrySignal.ShortTrend;
                LastBias = TradeType.Sell;
            }

            // =========================
            // LOGIC CONFIDENCE (SOFT SCORING)
            // =========================
            int conf = 50;

            // Trend exists
            if (signal != GerEntrySignal.None)
                conf += 10;

            if (adx >= AdxTrend)    // 17
                conf += 5;

            if (adx >= AdxStrong)   // 22
                conf += 10;

            if (adx >= 35)
                conf += 15;

            // EMA separation relative to ATR (soft)
            // GER40-en ATR nagy, de a kapcsolat hasznos: ha a trend "szétnyílt", jobb a bias.
            double emaAbs = Math.Abs(emaDiff);
            if (atr > 0 && emaAbs > atr * 0.3)
                conf += 10;

            // HTF alignment bonus (soft)
            // Long esetén jó, ha HTF is bull; short esetén jó, ha HTF inkább bear.
            if (signal == GerEntrySignal.LongTrend && htfBull)
                conf += 5;
            else if (signal == GerEntrySignal.ShortTrend && !htfBull)
                conf += 5;

            // Clamp
            conf = Math.Max(0, Math.Min(100, conf));
            LastLogicConfidence = conf;

            // =========================
            // DEBUG (INFORMATÍV)
            // =========================
            _bot.Print(
                $"[GER40 LOGIC] signal={signal} bias={LastBias} logicConf={LastLogicConfidence} | " +
                $"ema50={ema50:F2} ema200={ema200:F2} diff={emaDiff:F2} | " +
                $"adx={adx:F1} atr={atr:F2} | " +
                $"htfBull={htfBull}"
            );
        }

        // =========================================================
        // INTERNAL SIGNAL MODEL (NO EXTRA FILE)
        // =========================================================
        private enum GerEntrySignal
        {
            None = 0,
            LongTrend = 1,
            ShortTrend = 2
        }
    }
}
