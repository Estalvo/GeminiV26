using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.USDJPY
{
    /// <summary>
    /// USDJPY Entry Logic – stricter long continuation, earlier weakening shorts.
    /// </summary>
    public class UsdJpyEntryLogic : IEntryLogic
    {
        private readonly Robot _bot;

        public TradeType LastBias { get; private set; }
        public int LastLogicConfidence { get; private set; }

        public UsdJpyEntryLogic(Robot bot)
        {
            _bot = bot;
            LastBias = TradeType.Buy;
            LastLogicConfidence = 0;
        }

        public void Evaluate()
        {
            LastLogicConfidence = 0;

            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5);
            var m15 = _bot.MarketData.GetBars(TimeFrame.Minute15);
            if (m5 == null || m15 == null || m5.Count < 260 || m15.Count < 80)
                return;

            int i = m5.Count - 2;
            if (i < 12)
                return;

            var ema8 = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 8).Result;
            var ema21 = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 21).Result;
            var ema50 = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 50).Result;
            var ema200 = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 200).Result;
            var ema21M15 = _bot.Indicators.ExponentialMovingAverage(m15.ClosePrices, 21).Result;
            var atr = _bot.Indicators.AverageTrueRange(m5, 14, MovingAverageType.Exponential);
            var adx = _bot.Indicators.AverageDirectionalMovementIndexRating(14);

            double atrValue = atr.Result[i];
            if (atrValue <= 0)
                return;

            double close = m5.ClosePrices[i];
            double open = m5.OpenPrices[i];
            double high = m5.HighPrices[i];

            double ema8Now = ema8[i];
            double ema21Now = ema21[i];
            double ema50Now = ema50[i];
            double ema200Now = ema200[i];
            double ema21SlopeM5 = ema21[i] - ema21[i - 3];
            double ema21SlopeM15 = ema21M15.LastValue - ema21M15[Math.Max(0, m15.Count - 4)];
            double adxNow = adx.ADX[i];
            double adxSlope = adx.ADX[i] - adx.ADX[i - 3];

            double recentHigh4 = GetRecentHigh(m5, i - 1, 4);
            double recentLow4 = GetRecentLow(m5, i - 1, 4);
            double recentLow8 = GetRecentLow(m5, i, 8);

            bool bullishClose = close > open;
            bool bearishClose = close < open;
            bool bullishReclaim = bullishClose && close > ema8Now && close > ema21Now;
            bool bearishReclaim = bearishClose && close < ema8Now && close < ema21Now;

            bool trendLong =
                ema50Now > ema200Now &&
                ema21Now >= ema50Now &&
                ema21SlopeM5 > atrValue * 0.04 &&
                ema21SlopeM15 >= -atrValue * 0.02 &&
                close >= ema21Now;

            bool trendShort =
                ema50Now < ema200Now &&
                ema21Now <= ema50Now &&
                ema21SlopeM5 < -atrValue * 0.04 &&
                ema21SlopeM15 <= atrValue * 0.02 &&
                close <= ema21Now;

            bool higherLowConfirmed =
                low > GetRecentLow(m5, i - 3, 3) &&
                m5.LowPrices[i - 1] >= m5.LowPrices[i - 3] &&
                m5.HighPrices[i] >= m5.HighPrices[i - 2];

            bool lowerHighLowerLow =
                high < GetRecentHigh(m5, i - 3, 3) &&
                m5.HighPrices[i - 1] <= m5.HighPrices[i - 3] &&
                m5.LowPrices[i] <= m5.LowPrices[i - 2];

            double pullbackDepthLong = (ema21Now - GetRecentLow(m5, i, 5)) / atrValue;
            double pullbackDepthShort = (GetRecentHigh(m5, i, 5) - ema21Now) / atrValue;

            bool pullbackCompleteLong =
                pullbackDepthLong >= 0.18 &&
                pullbackDepthLong <= 1.10 &&
                GetRecentLow(m5, i, 4) <= ema21Now + atrValue * 0.08 &&
                bullishReclaim;

            bool continuationLong =
                higherLowConfirmed &&
                (close > recentHigh4 || (bullishReclaim && m5.ClosePrices[i - 1] >= ema21[i - 1]));

            bool extendedLong =
                (close - recentLow8) / atrValue >= 2.45 ||
                (close - ema21Now) / atrValue >= 1.20;

            bool exhaustionLong =
                adxNow >= 34.0 &&
                adxSlope <= 0 &&
                !pullbackCompleteLong;

            bool incompleteLongStructure = !higherLowConfirmed || !continuationLong || !pullbackCompleteLong;

            bool shortBreakdown = bearishClose && close < recentLow4;
            bool rejectionFailure =
                bearishClose &&
                high >= recentHigh4 - atrValue * 0.10 &&
                close < ema21Now;

            bool weakeningShort =
                lowerHighLowerLow ||
                shortBreakdown ||
                rejectionFailure ||
                (ema21SlopeM5 <= 0 && bearishReclaim && close < recentLow4 + atrValue * 0.15);

            bool earlyShortAllowed =
                bearishReclaim &&
                pullbackDepthShort >= 0.15 &&
                pullbackDepthShort <= 1.15 &&
                (lowerHighLowerLow || shortBreakdown || rejectionFailure);

            if (trendLong && continuationLong && pullbackCompleteLong && !extendedLong && !exhaustionLong && adxNow >= 18.0)
            {
                int confidence = 62;
                if (adxNow >= 22.0) confidence += 6;
                if (adxNow >= 28.0) confidence += 4;
                if (close > recentHigh4) confidence += 5;
                if (ema21SlopeM15 > 0) confidence += 4;
                if (higherLowConfirmed) confidence += 4;

                LastBias = TradeType.Buy;
                LastLogicConfidence = Math.Min(confidence, 92);
            }
            else if (weakeningShort && earlyShortAllowed)
            {
                int confidence = trendShort ? 63 : 58;
                if (shortBreakdown) confidence += 5;
                if (lowerHighLowerLow) confidence += 4;
                if (adxNow >= 18.0) confidence += 4;
                if (adxSlope >= 0) confidence += 2;

                LastBias = TradeType.Sell;
                LastLogicConfidence = Math.Min(confidence, 90);
            }
            else if (trendShort && bearishReclaim && !pullbackCompleteLong && !continuationLong)
            {
                int confidence = 55;
                if (lowerHighLowerLow) confidence += 4;
                if (shortBreakdown) confidence += 4;

                LastBias = TradeType.Sell;
                LastLogicConfidence = Math.Min(confidence, 84);
            }
            else if (trendLong && incompleteLongStructure)
            {
                LastBias = TradeType.Buy;
                LastLogicConfidence = 0;
            }

            _bot.Print(
                $"[USDJPY LOGIC] trend={(trendLong ? "Long" : trendShort ? "Short" : "None")} " +
                $"longCont={continuationLong} longPb={pullbackCompleteLong} ext={extendedLong} exh={exhaustionLong} " +
                $"shortWeak={weakeningShort} shortBreak={shortBreakdown} bias={LastBias} logicConf={LastLogicConfidence}");
        }

        private static double GetRecentHigh(Bars bars, int endIndex, int length)
        {
            int start = Math.Max(0, endIndex - length + 1);
            double value = double.MinValue;
            for (int idx = start; idx <= endIndex; idx++)
                value = Math.Max(value, bars.HighPrices[idx]);
            return value;
        }

        private static double GetRecentLow(Bars bars, int endIndex, int length)
        {
            int start = Math.Max(0, endIndex - length + 1);
            double value = double.MaxValue;
            for (int idx = start; idx <= endIndex; idx++)
                value = Math.Min(value, bars.LowPrices[idx]);
            return value;
        }
    }
}
