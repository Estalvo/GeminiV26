using cAlgo.API;
using cAlgo.API.Indicators;
using System;

namespace GeminiV26.Instruments.XAUUSD
{
    /// <summary>
    /// LEAN EntryLogic – Gemini V26
    /// --------------------------------
    /// Feladata:
    /// - trend bias meghatározása (EMA struktúra)
    /// - confidence számítás
    /// NEM:
    /// - signal builder
    /// - setup felismerő
    /// - gate / veto
    /// </summary>
    public sealed class XauEntryLogic
    {
        private readonly Robot _bot;

        // ===== Bars =====
        private readonly Bars _m5;
        private readonly Bars _m15;

        // ===== Indicators =====
        private readonly ExponentialMovingAverage _ema8_m5;
        private readonly ExponentialMovingAverage _ema21_m5;
        private readonly ExponentialMovingAverage _ema21_m15;

        // ===== Public state =====
        public TradeType LastBias { get; private set; } = TradeType.Buy;
        public int LastLogicConfidence { get; private set; } = 50;

        public XauEntryLogic(Robot bot)
        {
            _bot = bot;

            _m5 = bot.Bars;
            _m15 = bot.MarketData.GetBars(TimeFrame.Minute15);

            _ema8_m5 = bot.Indicators.ExponentialMovingAverage(_m5.ClosePrices, 8);
            _ema21_m5 = bot.Indicators.ExponentialMovingAverage(_m5.ClosePrices, 21);
            _ema21_m15 = bot.Indicators.ExponentialMovingAverage(_m15.ClosePrices, 21);
        }

        /// <summary>
        /// Evaluate – csak bias + confidence
        /// </summary>
        public void Evaluate(out TradeType bias, out int confidence)
        {
            int i5 = _m5.ClosePrices.Count - 1;
            int i15 = _m15.ClosePrices.Count - 1;

            if (i5 < 25 || i15 < 25)
            {
                bias = TradeType.Buy;
                confidence = 50;
                return;
            }

            // ===== EMA értékek =====
            double ema8 = _ema8_m5.Result[i5];
            double ema21 = _ema21_m5.Result[i5];
            double ema21Htf = _ema21_m15.Result[i15];
            double price = _m5.ClosePrices[i5];
            var sym = _bot.Symbol;

            // ===== Bias =====
            bool bullish =
                ema8 > ema21 &&
                price > ema21 &&
                price >= ema21Htf;

            bool bearish =
                ema8 < ema21 &&
                price < ema21 &&
                price <= ema21Htf;

            if (bullish)
                bias = TradeType.Buy;
            else if (bearish)
                bias = TradeType.Sell;
            else
                bias = LastBias; // nincs váltás zajban

            // ===== Confidence =====
            int conf = 50;

            // EMA spread (M5)
            double emaSpread = Math.Abs(ema8 - ema21) / sym.PipSize;
            if (emaSpread >= 15) conf += 10;
            if (emaSpread >= 30) conf += 5;

            // HTF alignment
            if (bullish || bearish)
                conf += 10;

            // Price positioning
            if (bias == TradeType.Buy && price > ema8)
                conf += 5;
            if (bias == TradeType.Sell && price < ema8)
                conf += 5;

            // Clamp
            conf = Math.Max(30, Math.Min(80, conf));

            // ===== Persist =====
            LastBias = bias;
            LastLogicConfidence = conf;

            _bot.Print(
                $"[XAU LOGIC] bias={bias} conf={conf} " +
                $"ema8={ema8:F2} ema21={ema21:F2} ema21HTF={ema21Htf:F2}"
            );

            confidence = conf;
        }
    }
}
