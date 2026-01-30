using cAlgo.API;
using cAlgo.API.Internals;
using System;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Instruments.METAL;
using GeminiV26.Core.HtfBias;
using GeminiV26.EntryTypes.METAL;

namespace GeminiV26.Core.Entry
{
    public class EntryContextBuilder
    {
        private readonly Robot _bot;
        private readonly CryptoHtfBiasEngine _cryptoHtf;
        private readonly FxHtfBiasEngine _fxHtf;
        private readonly IndexHtfBiasEngine _indexHtf;
        private readonly MetalHtfBiasEngine _metalHtf;

        public EntryContextBuilder(Robot bot)
        {
            _bot = bot;
            _cryptoHtf = new CryptoHtfBiasEngine(_bot);
            _fxHtf = new FxHtfBiasEngine(_bot);
            _indexHtf = new IndexHtfBiasEngine(_bot);
            _metalHtf = new MetalHtfBiasEngine(_bot);
        }

        // =================================================
        // INSTRUMENT CLASSIFICATION
        // =================================================
        private bool IsIndexSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;

            symbol = symbol.ToUpperInvariant();

            return
                symbol.Contains("NAS") ||
                symbol.Contains("USTECH") ||
                symbol.Contains("US TECH") ||
                symbol.Contains("US30") ||
                symbol.Contains("US 30") ||
                symbol.Contains("GER") ||
                symbol.Contains("GER40") ||
                symbol.Contains("GERMANY") ||
                symbol.Contains("DE40") ||
                symbol.Contains("DAX");
        }

        private bool IsCryptoSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;

            symbol = symbol.ToUpperInvariant();

            return
                symbol.Contains("BTC") ||
                symbol.Contains("ETH") ||
                symbol.Contains("CRYPTO");
        }

        private bool IsMetalSymbol(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return false;

            symbol = symbol.ToUpperInvariant();

            return
                symbol.Contains("XAU") ||
                symbol.Contains("XAG");
        }

        // =================================================
        // BUILD
        // =================================================
        public EntryContext Build(string symbol)
        {
            var ctx = new EntryContext
            {
                Symbol = symbol,
                IsReady = false,
                TrendDirection = TradeDirection.None
            };

            // -------------------------
            // BAR DATA
            // -------------------------
            ctx.M1 = _bot.MarketData.GetBars(TimeFrame.Minute, symbol);
            ctx.M5 = _bot.MarketData.GetBars(TimeFrame.Minute5, symbol);
            ctx.M15 = _bot.MarketData.GetBars(TimeFrame.Minute15, symbol);

            if (ctx.M1 == null || ctx.M5 == null || ctx.M15 == null)
                return ctx;

            if (ctx.M1.Count < 10 || ctx.M5.Count < 30 || ctx.M15.Count < 30)
                return ctx;

            int m1Idx = ctx.M1.Count - 2;
            int m5Idx = ctx.M5.Count - 2;
            int m15Idx = ctx.M15.Count - 2;

            // -------------------------
            // EMA
            // -------------------------
            var ema21_m5 = _bot.Indicators.ExponentialMovingAverage(ctx.M5.ClosePrices, 21);
            var ema21_m15 = _bot.Indicators.ExponentialMovingAverage(ctx.M15.ClosePrices, 21);

            ctx.Ema21_M5 = ema21_m5.Result.LastValue;
            ctx.Ema21_M15 = ema21_m15.Result.LastValue;

            ctx.Ema21Slope_M5 = ema21_m5.Result[m5Idx] - ema21_m5.Result[m5Idx - 3];
            ctx.Ema21Slope_M15 = ema21_m15.Result[m15Idx] - ema21_m15.Result[m15Idx - 3];

            // =================================================
            // HARD TREND
            // =================================================
            if (ctx.Ema21Slope_M15 > 0 && ctx.Ema21Slope_M5 > 0)
                ctx.TrendDirection = TradeDirection.Long;
            else if (ctx.Ema21Slope_M15 < 0 && ctx.Ema21Slope_M5 < 0)
                ctx.TrendDirection = TradeDirection.Short;

            // XAU/XAG fallback
            if (ctx.TrendDirection == TradeDirection.None && IsMetalSymbol(symbol))
            {
                if (ctx.Ema21Slope_M15 > 0)
                    ctx.TrendDirection = TradeDirection.Long;
                else if (ctx.Ema21Slope_M15 < 0)
                    ctx.TrendDirection = TradeDirection.Short;
            }

            // =================================================
            // INSTRUMENT FLAGS
            // =================================================
            bool isIndex = IsIndexSymbol(symbol);
            bool isCrypto = IsCryptoSymbol(symbol);
            bool isMetal = IsMetalSymbol(symbol);

            bool isFx =
                !isIndex &&
                !isCrypto &&
                !isMetal &&
                FxInstrumentMatrix.Contains(symbol);

            // -------------------------
            // ATR
            // -------------------------
            var atr_m5 = _bot.Indicators.AverageTrueRange(ctx.M5, 14, MovingAverageType.Simple);
            var atr_m15 = _bot.Indicators.AverageTrueRange(ctx.M15, 14, MovingAverageType.Simple);

            ctx.AtrM5 = atr_m5.Result.LastValue;
            ctx.AtrM15 = atr_m15.Result.LastValue;
            ctx.AtrPips_M5 = ctx.AtrM5 / _bot.Symbol.PipSize;

            // =================================================
            // PROFILES
            // =================================================
            FxInstrumentProfile fxProfile = isFx ? FxInstrumentMatrix.Get(symbol) : null;
            IndexInstrumentProfile indexProfile =
                isIndex && IndexInstrumentMatrix.Contains(symbol)
                    ? IndexInstrumentMatrix.Get(symbol)
                    : null;

            XAU_InstrumentProfile metalProfile =
                isMetal
                    ? XAU_InstrumentMatrix.Get(symbol)
                    : null;

            // =================================================
            // CRYPTO VOLATILITY REGIME
            // =================================================
            ctx.IsVolatilityAcceptable_Crypto = true;

            if (isCrypto)
            {
                if (ctx.AtrM5 <= 0 || ctx.AtrM15 <= 0)
                    ctx.IsVolatilityAcceptable_Crypto = false;
                else
                {
                    double atrRatio = ctx.AtrM5 / ctx.AtrM15;
                    ctx.IsVolatilityAcceptable_Crypto =
                        atrRatio >= 0.6 &&
                        atrRatio <= 1.8;
                }
            }

            // =================================================
            // IMPULSE M5  (PROFILE-AWARE)
            // =================================================
            double m5Body = Math.Abs(ctx.M5.ClosePrices[m5Idx] - ctx.M5.OpenPrices[m5Idx]);
            double m5Range = ctx.M5.HighPrices[m5Idx] - ctx.M5.LowPrices[m5Idx];

            if (isFx && fxProfile != null)
            {
                ctx.HasImpulse_M5 =
                    ctx.AtrM5 > 0 &&
                    m5Body > ctx.AtrM5 * fxProfile.ImpulseAtrMult_M5;
            }
            else if (isIndex && indexProfile != null)
            {
                ctx.HasImpulse_M5 =
                    ctx.AtrM5 > 0 &&
                    m5Body > ctx.AtrM5 * indexProfile.ImpulseAtrMult_M5;
            }
            else if (isMetal && metalProfile != null)
            {
                ctx.HasImpulse_M5 =
                    ctx.AtrM5 > 0 &&
                    (
                        m5Body > ctx.AtrM5 * metalProfile.SlAtrMultHigh * 0.08 ||
                        m5Range > ctx.AtrM5 * metalProfile.SlAtrMultHigh * 0.20
                    );
            }
            else
            {
                ctx.HasImpulse_M5 =
                    ctx.AtrM5 > 0 &&
                    m5Body > ctx.AtrM5 * 0.4;
            }

            // =================================================
            // BARS SINCE IMPULSE (M5)
            // =================================================
            ctx.BarsSinceImpulse_M5 = ctx.HasImpulse_M5 ? 0 : 999;

            if (!ctx.HasImpulse_M5)
            {
                for (int i = m5Idx - 1, bars = 1; i >= Math.Max(0, m5Idx - 10); i--, bars++)
                {
                    double body = Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]);

                    double impulseMult =
                        isFx && fxProfile != null ? fxProfile.ImpulseAtrMult_M5 :
                        isIndex && indexProfile != null ? indexProfile.ImpulseAtrMult_M5 :
                        isMetal && metalProfile != null ? metalProfile.SlAtrMultHigh * 0.08 :
                        0.4;

                    if (ctx.AtrM5 > 0 && body > ctx.AtrM5 * impulseMult)
                    {
                        ctx.BarsSinceImpulse_M5 = bars;
                        break;
                    }
                }
            }

            // =================================================
            // FLAG STRUCTURE M5
            // =================================================
            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = m5Idx - 4; i <= m5Idx; i++)
            {
                hi = Math.Max(hi, ctx.M5.HighPrices[i]);
                lo = Math.Min(lo, ctx.M5.LowPrices[i]);
            }

            double range5 = hi - lo;

            ctx.IsValidFlagStructure_M5 =
                ctx.AtrM5 > 0 &&
                range5 > 0 &&
                range5 < ctx.AtrM5 * 3.0;
            ctx.FlagAtr_M5 = range5;

            // =================================================
            // ATR EXPANSION
            // =================================================
            ctx.IsAtrExpanding_M5 =
                atr_m5.Result[m5Idx] > atr_m5.Result[m5Idx - 3];

            // =================================================
            // PULLBACK BARS (M5)
            // =================================================
            ctx.PullbackBars_M5 = 0;

            if (ctx.TrendDirection != TradeDirection.None)
            {
                for (int i = m5Idx; i >= Math.Max(0, m5Idx - 10); i--)
                {
                    // NOTE: i+1 is safe here because i starts at m5Idx (>=1) and we stop at >=0.
                    // m5Idx is Count-2 -> i+1 is within range.
                    bool againstTrend =
                        ctx.TrendDirection == TradeDirection.Long
                            ? ctx.M5.ClosePrices[i] < ctx.M5.ClosePrices[i + 1]
                            : ctx.M5.ClosePrices[i] > ctx.M5.ClosePrices[i + 1];

                    if (!againstTrend)
                        break;

                    ctx.PullbackBars_M5++;
                }
            }

            // =================================================
            // PULLBACK DEPTH (M5) - REAL MEASUREMENT
            // =================================================
            ctx.PullbackDepthAtr_M5 = 0.0;

            if (ctx.TrendDirection != TradeDirection.None &&
                ctx.PullbackBars_M5 > 0 &&
                ctx.AtrM5 > 0)
            {
                double extreme = ctx.TrendDirection == TradeDirection.Long
                    ? double.MaxValue
                    : double.MinValue;

                for (int i = m5Idx; i > m5Idx - ctx.PullbackBars_M5; i--)
                {
                    if (ctx.TrendDirection == TradeDirection.Long)
                        extreme = Math.Min(extreme, ctx.M5.LowPrices[i]);
                    else
                        extreme = Math.Max(extreme, ctx.M5.HighPrices[i]);
                }

                double reference =
                    ctx.TrendDirection == TradeDirection.Long
                        ? ctx.M5.ClosePrices[m5Idx + 1]
                        : ctx.M5.ClosePrices[m5Idx + 1];

                ctx.PullbackDepthAtr_M5 =
                    Math.Abs(reference - extreme) / ctx.AtrM5;
            }

            // =================================================
            // PULLBACK DECELERATION (M5)
            // =================================================
            ctx.IsPullbackDecelerating_M5 = false;
            ctx.AvgBodyLast3_M5 = 0.0;
            ctx.AvgBodyPrev3_M5 = 0.0;

            if (ctx.TrendDirection != TradeDirection.None && ctx.PullbackBars_M5 >= 2 && ctx.AtrM5 > 0)
            {
                // We have ctx.M5.Count >= 30 => m5Idx >= 28 => m5Idx-5 >= 23 safe.
                double body0 = Math.Abs(ctx.M5.ClosePrices[m5Idx] - ctx.M5.OpenPrices[m5Idx]);
                double body1 = Math.Abs(ctx.M5.ClosePrices[m5Idx - 1] - ctx.M5.OpenPrices[m5Idx - 1]);
                double body2 = Math.Abs(ctx.M5.ClosePrices[m5Idx - 2] - ctx.M5.OpenPrices[m5Idx - 2]);

                double body3 = Math.Abs(ctx.M5.ClosePrices[m5Idx - 3] - ctx.M5.OpenPrices[m5Idx - 3]);
                double body4 = Math.Abs(ctx.M5.ClosePrices[m5Idx - 4] - ctx.M5.OpenPrices[m5Idx - 4]);
                double body5 = Math.Abs(ctx.M5.ClosePrices[m5Idx - 5] - ctx.M5.OpenPrices[m5Idx - 5]);

                ctx.AvgBodyLast3_M5 = (body0 + body1 + body2) / 3.0;
                ctx.AvgBodyPrev3_M5 = (body3 + body4 + body5) / 3.0;

                ctx.IsPullbackDecelerating_M5 =
                    ctx.AvgBodyLast3_M5 < ctx.AvgBodyPrev3_M5 &&
                    ctx.AvgBodyLast3_M5 < ctx.AtrM5 * 0.6;
            }

            // =================================================
            // REJECTION WICK (M5)  -> confirms actual rejection, not just a small candle
            // =================================================
            ctx.HasRejectionWick_M5 = false;

            if (ctx.TrendDirection != TradeDirection.None)
            {
                double o = ctx.M5.OpenPrices[m5Idx];
                double c = ctx.M5.ClosePrices[m5Idx];
                double h2 = ctx.M5.HighPrices[m5Idx];
                double l2 = ctx.M5.LowPrices[m5Idx];

                double body = Math.Abs(c - o);
                double upperWick = h2 - Math.Max(o, c);
                double lowerWick = Math.Min(o, c) - l2;

                // Basic sanity
                if (body < 1e-12) body = 1e-12;

                if (ctx.TrendDirection == TradeDirection.Long)
                {
                    // bullish candle with notable lower rejection
                    ctx.HasRejectionWick_M5 =
                        c > o &&
                        lowerWick > body * 1.0;
                }
                else // Short trend
                {
                    // bearish candle with notable upper rejection
                    ctx.HasRejectionWick_M5 =
                        c < o &&
                        upperWick > body * 1.0;
                }
            }

            // =================================================
            // REACTION CANDLE (M5)
            // =================================================
            ctx.HasReactionCandle_M5 = false;

            if (ctx.TrendDirection != TradeDirection.None && ctx.AtrM5 > 0)
            {
                double currBody = Math.Abs(ctx.M5.ClosePrices[m5Idx] - ctx.M5.OpenPrices[m5Idx]);

                bool closesInTrendDirection =
                    ctx.TrendDirection == TradeDirection.Long
                        ? ctx.M5.ClosePrices[m5Idx] > ctx.M5.OpenPrices[m5Idx]
                        : ctx.M5.ClosePrices[m5Idx] < ctx.M5.OpenPrices[m5Idx];

                ctx.HasReactionCandle_M5 =
                    ctx.IsPullbackDecelerating_M5 &&
                    closesInTrendDirection &&
                    currBody < ctx.AtrM5 * 0.7 &&
                    ctx.HasRejectionWick_M5;
            }

            // =================================================
            // LAST CLOSED BAR IN TREND DIRECTION (M5)
            // =================================================
            ctx.LastClosedBarInTrendDirection = false;

            if (ctx.TrendDirection != TradeDirection.None)
            {
                // utolsó LEZÁRT M5 gyertya
                double o = ctx.M5.OpenPrices[m5Idx];
                double c = ctx.M5.ClosePrices[m5Idx];

                bool bullish = c > o;
                bool bearish = c < o;

                ctx.LastClosedBarInTrendDirection =
                    (ctx.TrendDirection == TradeDirection.Long && bullish) ||
                    (ctx.TrendDirection == TradeDirection.Short && bearish);
            }

            // =================================================
            // M1 BREAKOUT (unchanged)
            // =================================================
            double m1Hi = double.MinValue;
            double m1Lo = double.MaxValue;

            for (int i = m1Idx - 5; i <= m1Idx - 1; i++)
            {
                m1Hi = Math.Max(m1Hi, ctx.M1.HighPrices[i]);
                m1Lo = Math.Min(m1Lo, ctx.M1.LowPrices[i]);
            }

            double m1Close = ctx.M1.ClosePrices[m1Idx];

            ctx.HasBreakout_M1 = false;
            ctx.BreakoutDirection = TradeDirection.None;

            if (m1Close > m1Hi)
            {
                ctx.HasBreakout_M1 = true;
                ctx.BreakoutDirection = TradeDirection.Long;
            }
            else if (m1Close < m1Lo)
            {
                ctx.HasBreakout_M1 = true;
                ctx.BreakoutDirection = TradeDirection.Short;
            }
            
            ctx.M1TriggerInTrendDirection =
                ctx.HasBreakout_M1 &&
                ctx.BreakoutDirection == ctx.TrendDirection;

            // =================================================
            // HTF BIAS – FINAL DISPATCH (Phase 3.8 FIX)
            // =================================================

            if (isCrypto)
            {
                var htf = _cryptoHtf.Get(symbol);
                ctx.CryptoHtfAllowedDirection = htf.AllowedDirection;
                ctx.CryptoHtfConfidence01 = htf.Confidence01;
                ctx.CryptoHtfReason = htf.Reason;
            }
            else if (isFx)
            {
                var htf = _fxHtf.Get(symbol);
                ctx.FxHtfAllowedDirection = htf.AllowedDirection;
                ctx.FxHtfConfidence01 = htf.Confidence01;
                ctx.FxHtfReason = htf.Reason;
            }
            else if (isIndex)
            {
                var htf = _indexHtf.Get(symbol);
                ctx.IndexHtfAllowedDirection = htf.AllowedDirection;
                ctx.IndexHtfConfidence01 = htf.Confidence01;
                ctx.IndexHtfReason = htf.Reason;
            }
            else if (isMetal)
            {
                var htf = _metalHtf.Get(symbol);
                ctx.MetalHtfAllowedDirection = htf.AllowedDirection;
                ctx.MetalHtfConfidence01 = htf.Confidence01;
                ctx.MetalHtfReason = htf.Reason;
            }

            ctx.IsReady = true;
            return ctx;
        }
    }
}
