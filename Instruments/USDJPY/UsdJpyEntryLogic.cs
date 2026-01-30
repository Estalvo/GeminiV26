using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core.Entry;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.USDJPY
{
    /// <summary>
    /// USDJPY Entry Logic – Phase 3.7
    /// STRUKTÚRA: US30 klón (csak irány + confidence)
    /// LOGIKA: USDJPY trend/pullback jelleghez igazítva (FX noise kezelés)
    /// </summary>
    public class UsdJpyEntryLogic : IEntryLogic
    {
        private readonly Robot _bot;

        public TradeType LastBias { get; private set; }
        public int LastLogicConfidence { get; private set; }

        public UsdJpyEntryLogic(Robot bot)
        {
            _bot = bot;
            LastBias = TradeType.Buy;   // default, de confidence = 0 → nincs trade
            LastLogicConfidence = 0;
        }

        public void Evaluate()
        {
            LastLogicConfidence = 0;

            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5);
            if (m5 == null || m5.Count < 250)
                return;

            var ema50 = _bot.Indicators
                .ExponentialMovingAverage(m5.ClosePrices, 50)
                .Result.LastValue;

            var ema200 = _bot.Indicators
                .ExponentialMovingAverage(m5.ClosePrices, 200)
                .Result.LastValue;

            if (ema50 > ema200)
                LastBias = TradeType.Buy;
            else if (ema50 < ema200)
                LastBias = TradeType.Sell;
            else
                return;

            var adx = _bot.Indicators
                .AverageDirectionalMovementIndexRating(14)
                .ADX.LastValue;

            int conf = 60;
            if (adx > 25) conf += 10;
            if (adx > 30) conf += 5;

            LastLogicConfidence = Math.Min(conf, 95);
        }
    }

}
