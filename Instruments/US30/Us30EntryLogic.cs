using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core.Entry;

namespace GeminiV26.Instruments.US30
{
    /// <summary>
    /// US30 Entry Logic – Phase 3.6 (BASELINE, NAS 1:1 clone)
    /// Csak belépési döntés:
    /// - irány
    /// - confidence
    /// SOFT impulse check-kel
    /// NEM számol money risk / SL / TP-t.
    /// </summary>
    public class Us30EntryLogic
    {
        private readonly Robot _bot;

        public int LastLogicConfidence { get; private set; }

        public Us30EntryLogic(Robot bot)
        {
            _bot = bot;
        }

        public bool CheckEntry(
            out TradeDirection direction,
            out int confidence)
        {
            direction = TradeDirection.None;
            confidence = 0;

            // =========================
            // M5 Bars
            // =========================
            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5);
            if (m5 == null || m5.Count < 250)
                return false;

            // =========================
            // EMA50 / EMA200 – trend irány
            // =========================
            var ema50Ind = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 50);
            var ema200Ind = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 200);

            double ema50 = ema50Ind.Result.LastValue;
            double ema200 = ema200Ind.Result.LastValue;

            if (ema50 > ema200)
                direction = TradeDirection.Long;
            else if (ema50 < ema200)
                direction = TradeDirection.Short;
            else
                return false;

            // =========================
            // ADX – trend erő
            // =========================
            var adxInd = _bot.Indicators.AverageDirectionalMovementIndexRating(14);
            double adx = adxInd.ADX.LastValue;

            if (adx < 18)
                return false;

            // =========================
            // ATR – skálázás
            // =========================
            var atrInd = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            double atr = atrInd.Result.LastValue;

            // =========================
            // Confidence (v1) – NAS 1:1
            // =========================
            confidence = 60;

            if (adx > 25)
                confidence += 10;

            double emaDistance = Math.Abs(ema50 - ema200);
            if (atr > 0 && emaDistance > atr * 0.5)
                confidence += 10;

            if (confidence > 90)
                confidence = 90;

            // =========================
            // SOFT IMPULSE CHECK (Phase 3.6 BASELINE)
            // =========================
            bool impulseSoft = CheckSoftImpulse(m5);

            bool valid =
                confidence >= 70 &&
                impulseSoft;

            if (!valid)
                return false;

            return true;
        }

        // =========================
        // SOFT IMPULSE CHECK (US30, NAS 1:1 baseline)
        // =========================
        private bool CheckSoftImpulse(Bars bars)
        {
            int i = bars.ClosePrices.Count - 1;

            double body = Math.Abs(bars.ClosePrices[i] - bars.OpenPrices[i]);
            double range = bars.HighPrices[i] - bars.LowPrices[i];

            if (range <= 0)
                return false;

            // baseline: NAS-lazább arány
            return body / range >= 0.55;
        }

        // =========================
        // ADAPTER – EXECUTOR KOMPATIBILITÁS
        // =========================
        public void Evaluate()
        {
            if (CheckEntry(out _, out int conf))
                LastLogicConfidence = conf;
            else
                LastLogicConfidence = 0;
        }
    }
}
