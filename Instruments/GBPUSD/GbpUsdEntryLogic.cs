using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core.Entry;

namespace GeminiV26.Instruments.GBPUSD
{
    /// <summary>
    /// GBPUSD Entry Logic – Phase 3.7
    /// STRUKTÚRA: US30 klón (irány + confidence)
    /// LOGIKA: FX (GBP) → zaj + news spike érzékenyebb
    /// </summary>
    public class GbpUsdEntryLogic
    {
        private readonly Robot _bot;

        public int LastLogicConfidence { get; private set; }

        public GbpUsdEntryLogic(Robot bot)
        {
            _bot = bot;
        }

        public bool CheckEntry(out TradeDirection direction, out int confidence)
        {
            direction = TradeDirection.None;
            confidence = 0;

            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5);
            if (m5 == null || m5.Count < 250)
                return false;

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

            // ADX – trend erő (GBPUSD: kicsit magasabb küszöb, mert noise)
            var adxInd = _bot.Indicators.AverageDirectionalMovementIndexRating(14);
            double adx = adxInd.ADX.LastValue;

            if (adx < 19)
                return false;

            // ATR – skálázás + spike szűrés
            var atrInd = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            double atr = atrInd.Result.LastValue;
            if (atr <= 0)
                return false;

            // GBPUSD news-spike proxy: ha az utolsó gyertya túl nagy → tilt
            if (IsNewsSpikeBar(m5, atr))
                return false;

            // Confidence
            confidence = 58;

            if (adx > 24) confidence += 10;
            if (adx > 28) confidence += 5;

            double emaDistance = Math.Abs(ema50 - ema200);
            // FX: kisebb separation is elég, de ne legyen “flat”
            if (emaDistance > atr * 0.35) confidence += 10;
            if (emaDistance > atr * 0.50) confidence += 5;

            if (!SoftPullbackOk(m5, direction))
                confidence -= 8;

            // Soft impulse check (GBPUSD: kissé szigorúbb, mert noise)
            bool impulseSoft = CheckSoftImpulse(m5);

            if (confidence > 92) confidence = 92;

            // Valid belépés: minimum confidence + impulse
            return confidence >= 70 && impulseSoft;
        }

        private bool IsNewsSpikeBar(Bars m5, double atr)
        {
            int i = m5.Count - 1;
            if (i < 2) return false;

            double range = m5.HighPrices[i] - m5.LowPrices[i];
            // ha 1 gyertya ATR * 1.8 felett → tipikus spike
            return range > atr * 1.80;
        }

        private bool SoftPullbackOk(Bars m5, TradeDirection dir)
        {
            int i = m5.Count - 1;
            if (i < 4) return true;

            int bearish = 0;
            int bullish = 0;

            for (int k = 1; k <= 3; k++)
            {
                int idx = i - k;
                double o = m5.OpenPrices[idx];
                double c = m5.ClosePrices[idx];
                if (c < o) bearish++;
                if (c > o) bullish++;
            }

            if (dir == TradeDirection.Long)
                return bearish <= 2;

            return bullish <= 2;
        }

        private bool CheckSoftImpulse(Bars bars)
        {
            int i = bars.Count - 1;

            double body = Math.Abs(bars.ClosePrices[i] - bars.OpenPrices[i]);
            double range = bars.HighPrices[i] - bars.LowPrices[i];

            if (range <= 0)
                return false;

            // GBPUSD: szigorúbb arány, hogy ne wick-noise legyen
            return body / range >= 0.58;
        }
        public void Evaluate()
        {
            CheckEntry(out _, out int confidence);
            LastLogicConfidence = confidence;
        }

    }
}
