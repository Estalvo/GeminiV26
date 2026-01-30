// =========================================================
// GEMINI V26 – ETHUSD EntryLogic
// Rulebook 1.0 COMPLIANT
//
// Szerep:
// - Instrument-specifikus BIAS és LOGIC CONFIDENCE számítása
// - NEM gate
// - NEM veto
// - NEM dönt belépésről
//
// EntryLogic kizárólag információt ad:
// - TradeDirection bias (Long / Short / None)
// - LogicConfidence (0–100)
//
// Mismatch vagy gyenge jel:
// - NEM blockol
// - confidence csökkentés + log
// =========================================================

using System;
using cAlgo.API;
using GeminiV26.Core.Entry;

namespace GeminiV26.Instruments.ETHUSD
{
    public class EthUsdEntryLogic
    {
        private readonly Robot _bot;

        public EthUsdEntryLogic(Robot bot)
        {
            _bot = bot;
        }

        /// <summary>
        /// Calculates ETH-specific directional bias and logic confidence.
        /// This method NEVER blocks a trade.
        /// </summary>
        public void Evaluate(out TradeDirection biasDirection, out int logicConfidence)
        {
            biasDirection = TradeDirection.None;
            logicConfidence = 50; // neutral baseline

            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5);
            if (m5 == null || m5.Count < 200)
            {
                _bot.Print("[ETH][Logic] Not enough M5 bars → neutral bias");
                return;
            }

            // =========================================================
            // 1️⃣ TREND BIAS (EMA50 vs EMA200)
            // =========================================================
            var ema50 = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 50).Result.LastValue;
            var ema200 = _bot.Indicators.ExponentialMovingAverage(m5.ClosePrices, 200).Result.LastValue;

            if (ema50 > ema200)
            {
                biasDirection = TradeDirection.Long;
                logicConfidence += 10;
            }
            else if (ema50 < ema200)
            {
                biasDirection = TradeDirection.Short;
                logicConfidence += 10;
            }
            else
            {
                _bot.Print("[ETH][Logic] EMA50 ≈ EMA200 → no directional bias");
            }

            // =========================================================
            // 2️⃣ VOLATILITY CONTEXT (ATR sanity)
            // =========================================================
            var atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Simple).Result.LastValue;
            if (atr <= 0)
            {
                _bot.Print("[ETH][Logic] ATR invalid → confidence reduced");
                logicConfidence -= 10;
                return;
            }

            // =========================================================
            // 3️⃣ LAST CANDLE IMPULSE CHARACTER
            // =========================================================
            int i = m5.Count - 2;
            double body = Math.Abs(m5.ClosePrices[i] - m5.OpenPrices[i]);
            double range = m5.HighPrices[i] - m5.LowPrices[i];

            if (range <= 0)
            {
                _bot.Print("[ETH][Logic] Candle range invalid → confidence reduced");
                logicConfidence -= 10;
                return;
            }

            double bodyRatio = body / range;

            if (bodyRatio >= 0.65)
            {
                logicConfidence += 10;
                _bot.Print("[ETH][Logic] Strong impulse candle");
            }
            else
            {
                logicConfidence -= 5;
                _bot.Print("[ETH][Logic] Weak impulse candle");
            }

            // ATR-based impulse confirmation
            if (body > atr * 0.9) logicConfidence += 5;
            if (body > atr * 1.2) logicConfidence += 5;

            // =========================================================
            // 4️⃣ CLAMP CONFIDENCE
            // =========================================================
            logicConfidence = Math.Max(30, Math.Min(95, logicConfidence));

            _bot.Print($"[ETH][Logic] Bias={biasDirection} LogicConfidence={logicConfidence}");
        }
    }
}
