using GeminiV26.Core.HtfBias;
using GeminiV26.Core.Entry;
using cAlgo.API;
using cAlgo.API.Indicators;
using System;

namespace GeminiV26.Core.HtfBias
{
    public sealed class CryptoHtfBiasEngine : IHtfBiasProvider
    {
        private readonly Robot _bot;
        private static readonly TimeFrame HTF = TimeFrame.Hour4;

        // --- HTF thresholds (crypto-friendly) ---
        private const double MinAdxTrend = 18.0;
        private const double MinSlopePips = 0.15;

        public CryptoHtfBiasEngine(Robot bot)
        {
            _bot = bot;
        }

        public HtfBiasSnapshot Get(string symbolName)
        {
            var bars = _bot.MarketData.GetBars(HTF, symbolName);
            if (bars.Count < 300)
            {
                return new HtfBiasSnapshot
                {
                    State = HtfBiasState.Neutral,
                    AllowedDirection = TradeDirection.None,
                    Confidence01 = 0.0,
                    Reason = "HTF_NOT_READY"
                };
            }


            int i = bars.Count - 1;

            var ema50 = _bot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 50);
            var ema200 = _bot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 200);
            var dms = _bot.Indicators.DirectionalMovementSystem(bars, 14);
            var ema21 = _bot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 21);
            double ema21Now = ema21.Result[i];

            double ema50Now = ema50.Result[i];
            double ema50Prev = ema50.Result[i - 8];
            double ema200Now = ema200.Result[i];
            double adxVal = dms.ADX.Result[i];
            double price = bars.ClosePrices[i];

            var sym = _bot.Symbols.GetSymbol(symbolName);
            double slope = (ema50Now - ema50Prev) / sym.PipSize;

            bool strongTrend = adxVal >= MinAdxTrend;
            bool slopeUp = slope > MinSlopePips;
            bool slopeDown = slope < -MinSlopePips;

            // ===== BULL TREND =====
            if (ema50Now > ema200Now &&
                slopeUp &&
                strongTrend &&
                (price >= ema21Now || adxVal >= 25))
            {
                double x = Math.Max(0, adxVal - 15);
                double confidence = Math.Min(1.0, Math.Pow(x / 25.0, 0.7));

                return new HtfBiasSnapshot
                {
                    State = HtfBiasState.Bull,
                    AllowedDirection = TradeDirection.Long,
                    Confidence01 = confidence,
                    Reason = "HTF_BULL_TREND"
                };
            }

            // ===== BEAR TREND =====
            if (ema50Now < ema200Now &&
                slopeDown &&
                strongTrend &&
                (price <= ema21Now || adxVal >= 25))
            {
                double x = Math.Max(0, adxVal - 15);
                double confidence = Math.Min(1.0, Math.Pow(x / 25.0, 0.7));

                return new HtfBiasSnapshot
                {
                    State = HtfBiasState.Bear,
                    AllowedDirection = TradeDirection.Short,
                    Confidence01 = confidence,
                    Reason = "HTF_BEAR_TREND"
                };
            }

            // ===== WEAK STRUCTURE CONTINUATION =====
            if (strongTrend)
            {
                if (ema50Now > ema200Now && slopeUp)
                {
                    return new HtfBiasSnapshot
                    {
                        State = HtfBiasState.Bull,
                        AllowedDirection = TradeDirection.Long,
                        Confidence01 = 0.4,
                        Reason = "HTF_WEAK_BULL"
                    };
                }

                if (ema50Now < ema200Now && slopeDown)
                {
                    return new HtfBiasSnapshot
                    {
                        State = HtfBiasState.Bear,
                        AllowedDirection = TradeDirection.Short,
                        Confidence01 = 0.4,
                        Reason = "HTF_WEAK_BEAR"
                    };
                }
            }

            // ===== TRANSITION =====
            if (adxVal < MinAdxTrend)
            {
                return new HtfBiasSnapshot
                {
                    State = HtfBiasState.Transition,
                    AllowedDirection = TradeDirection.None,
                    Confidence01 = 0.25,
                    Reason = "HTF_ADX_WEAK"
                };
            }

            return new HtfBiasSnapshot
            {
                State = HtfBiasState.Neutral,
                AllowedDirection = TradeDirection.None,
                Confidence01 = 0.0,
                Reason = "HTF_NO_CLEAR_BIAS"
            };
        }
    }
}
