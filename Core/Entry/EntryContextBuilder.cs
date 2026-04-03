using cAlgo.API;
using cAlgo.API.Internals;
using System;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Instruments.METAL;
using GeminiV26.Core.HtfBias;
using GeminiV26.EntryTypes.METAL;
using GeminiV26.Core;
using Gemini.Memory;

namespace GeminiV26.Core.Entry
{
    public class EntryContextBuilder
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;
        private readonly CryptoHtfBiasEngine _cryptoHtf;
        private readonly FxHtfBiasEngine _fxHtf;
        private readonly IndexHtfBiasEngine _indexHtf;
        private readonly MetalHtfBiasEngine _metalHtf;
        private readonly MarketMemoryEngine _memoryEngine;

        public EntryContextBuilder(Robot bot, MarketMemoryEngine memoryEngine)
        {
            _bot = bot;
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
            _cryptoHtf = new CryptoHtfBiasEngine(_bot);
            _fxHtf = new FxHtfBiasEngine(_bot);
            _indexHtf = new IndexHtfBiasEngine(_bot);
            _metalHtf = new MetalHtfBiasEngine(_bot);
            _memoryEngine = memoryEngine;
        }

        public static bool GetHasEarlyPullback_M5(EntryContext ctx)
        {
            return ctx != null && ctx.HasEarlyPullback_M5;
        }

        // =================================================
        // INSTRUMENT CLASSIFICATION
        // =================================================
        private bool IsIndexSymbol(string symbol)
        {
            return SymbolRouting.ResolveInstrumentClass(symbol) == InstrumentClass.INDEX;
        }

        private bool IsCryptoSymbol(string symbol)
        {
            return SymbolRouting.ResolveInstrumentClass(symbol) == InstrumentClass.CRYPTO;
        }

        private bool IsMetalSymbol(string symbol)
        {
            return SymbolRouting.ResolveInstrumentClass(symbol) == InstrumentClass.METAL;
        }

        // =================================================
        // BUILD
        // =================================================
        public EntryContext Build(string symbol)
        {
            var ctx = new EntryContext
            {
                Symbol = symbol,
                TempId = CreateEntryAttemptId(symbol),
                IsReady = false,
                TrendDirection = TradeDirection.None,
                Log = message => GeminiV26.Core.Logging.GlobalLogger.Log(_bot, message)
            };

            string canonicalSymbol = SymbolRouting.NormalizeSymbol(symbol);
            GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[MEMORY][CTX_ATTACH][START] symbol={canonicalSymbol}");
            var memory = _memoryEngine.GetState(canonicalSymbol);

            if (memory == null)
            {
                ctx.Memory = null;
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[MEMORY][MISSING] symbol={canonicalSymbol}");
            }
            else
            {
                ctx.Memory = memory;
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[MEMORY][CTX_ATTACH] symbol={canonicalSymbol} hasMemory=true phase={memory.MovePhase} isBuilt={memory.IsBuilt} isUsable={memory.IsUsable}");
            }

            AttachMemorySnapshot(ctx, canonicalSymbol);

            // -------------------------
            // BAR DATA
            // -------------------------
            if (!_runtimeSymbols.TryGetBars(TimeFrame.Minute, symbol, out var m1) ||
                !_runtimeSymbols.TryGetBars(TimeFrame.Minute5, symbol, out var m5) ||
                !_runtimeSymbols.TryGetBars(TimeFrame.Minute15, symbol, out var m15))
            {
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[RESOLVER][ENTRY_BLOCK] symbol={symbol} reason=unresolved_runtime_symbol");
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][EARLY_RETURN] symbol={symbol} reason=unresolved_runtime_symbol");
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][MEMORY_READY] symbol={symbol} hasMemory={ctx.HasMemory}");
                LogEntryMemorySnapshot(ctx, symbol);
                return ctx;
            }

            ctx.M1 = m1;
            ctx.M5 = m5;
            ctx.M15 = m15;
            ctx.RuntimeResolved = true;

            if (ctx.M1.Count < 10 || ctx.M5.Count < 30 || ctx.M15.Count < 30)
            {
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][EARLY_RETURN] symbol={symbol} reason=insufficient_bars");
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][MEMORY_READY] symbol={symbol} hasMemory={ctx.HasMemory}");
                LogEntryMemorySnapshot(ctx, symbol);
                return ctx;
            }

            int m1Idx = ctx.M1.Count - 2;
            int m5Idx = ctx.M5.Count - 2;
            int m15Idx = ctx.M15.Count - 2;

            // =====================================================
            // ATR (M5 + M15)
            // =====================================================

            var atr_m5 = _bot.Indicators.AverageTrueRange(ctx.M5, 14, MovingAverageType.Simple);
            var atr_m15 = _bot.Indicators.AverageTrueRange(ctx.M15, 14, MovingAverageType.Simple);

            // aktuális index
            ctx.AtrM5 = atr_m5.Result[m5Idx];
            ctx.AtrM15 = atr_m15.Result.LastValue;

            // --- ATR slope (trend volatility iránya)
            ctx.AtrSlope_M5 = atr_m5.Result[m5Idx] - atr_m5.Result[m5Idx - 2];

            // --- ATR acceleration (opcionális – volatility gyorsul-e vagy lassul)
            ctx.AtrAcceleration_M5 =
                (atr_m5.Result[m5Idx] - atr_m5.Result[m5Idx - 2]) -
                (atr_m5.Result[m5Idx - 2] - atr_m5.Result[m5Idx - 4]);

            // pip konverzió
            if (!_runtimeSymbols.TryGetPipSize(symbol, out double pipSize) ||
                !_runtimeSymbols.TryGetSymbolMeta(symbol, out _))
            {
                ctx.RuntimeResolved = false;
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[RESOLVER][ENTRY_BLOCK] symbol={symbol} reason=unresolved_runtime_symbol");
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][EARLY_RETURN] symbol={symbol} reason=unresolved_runtime_symbol");
                GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][MEMORY_READY] symbol={symbol} hasMemory={ctx.HasMemory}");
                LogEntryMemorySnapshot(ctx, symbol);
                return ctx;
            }

            ctx.PipSize = pipSize;

            ctx.AtrPips_M5 = (pipSize > 0)
                ? (ctx.AtrM5 / pipSize)
                : 0;


            // =====================================================
            // ADX / DMS (M5)
            // =====================================================

            var dms = _bot.Indicators.DirectionalMovementSystem(ctx.M5, 14);

            // --- ADX aktuális érték
            ctx.Adx_M5 = dms.ADX[m5Idx];

            // --- ADX slope (trend erősödik vagy gyengül)
            ctx.AdxSlope_M5 = dms.ADX[m5Idx] - dms.ADX[m5Idx - 2];

            // --- ADX acceleration (trend gyorsul vagy kifullad)
            ctx.AdxAcceleration_M5 =
                (dms.ADX[m5Idx] - dms.ADX[m5Idx - 2]) -
                (dms.ADX[m5Idx - 2] - dms.ADX[m5Idx - 4]);

            // --- Directional bias
            ctx.PlusDI_M5 = dms.DIPlus[m5Idx];
            ctx.MinusDI_M5 = dms.DIMinus[m5Idx];

            // --- DI spread (irányerő különbség)
            ctx.DiSpread_M5 = ctx.PlusDI_M5 - ctx.MinusDI_M5;

            // -------------------------
            // EMA (MIND LAST CLOSED INDEXRŐL)
            // -------------------------
            var ema21_m5 = _bot.Indicators.ExponentialMovingAverage(ctx.M5.ClosePrices, 21);
            var ema21_m15 = _bot.Indicators.ExponentialMovingAverage(ctx.M15.ClosePrices, 21);

            var ema50M5 = _bot.Indicators.ExponentialMovingAverage(ctx.M5.ClosePrices, 50);
            var ema200M5 = _bot.Indicators.ExponentialMovingAverage(ctx.M5.ClosePrices, 200);

            var ema50M15 = _bot.Indicators.ExponentialMovingAverage(ctx.M15.ClosePrices, 50);
            var ema200M15 = _bot.Indicators.ExponentialMovingAverage(ctx.M15.ClosePrices, 200);

            ctx.Ema21_M5 = ema21_m5.Result[m5Idx];
            ctx.Ema21_M15 = ema21_m15.Result[m15Idx];

            ctx.Ema50_M5 = ema50M5.Result[m5Idx];
            ctx.Ema200_M5 = ema200M5.Result[m5Idx];

            ctx.Ema50_M15 = ema50M15.Result[m15Idx];
            ctx.Ema200_M15 = ema200M15.Result[m15Idx];

            // slope = last closed diff
            ctx.Ema21Slope_M5 = ema21_m5.Result[m5Idx] - ema21_m5.Result[m5Idx - 3];
            ctx.Ema21Slope_M15 = ema21_m15.Result[m15Idx] - ema21_m15.Result[m15Idx - 3];

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
                FxInstrumentMatrix.Contains(SymbolRouting.NormalizeSymbol(symbol));

            // -------------------------
            // HARD TREND (MOST MÁR ATR KÉSZ)
            // -------------------------
            ctx.TrendDirection = TradeDirection.None;

            if (ctx.AtrM5 > 0 && ctx.AtrM15 > 0)
            {
                double slopeM5 = ctx.Ema21Slope_M5 / ctx.AtrM5;
                double slopeM15 = ctx.Ema21Slope_M15 / ctx.AtrM15;

                // instrument-aware deadzone
                double slopeDeadzone =
                    isCrypto ? 0.02 :
                    isFx     ? 0.035 :
                    isIndex  ? 0.035 :
                    isMetal  ? 0.04 :
                            0.05;

                if (slopeM15 > slopeDeadzone && slopeM5 > slopeDeadzone)
                    ctx.TrendDirection = TradeDirection.Long;
                else if (slopeM15 < -slopeDeadzone && slopeM5 < -slopeDeadzone)
                    ctx.TrendDirection = TradeDirection.Short;
            }

            // =================================================
            // CRYPTO TREND OVERRIDE (DI-dominant in strong trend)
            // =================================================

            if (isCrypto && ctx.Adx_M5 >= 25 && Math.Abs(ctx.DiSpread_M5) >= 4)
            {
                ctx.TrendDirection =
                    ctx.DiSpread_M5 > 0
                        ? TradeDirection.Long
                        : TradeDirection.Short;
            }

            // XAU/XAG fallback
            if (ctx.TrendDirection == TradeDirection.None && IsMetalSymbol(symbol))
            {
                if (ctx.Ema21Slope_M15 > 0)
                    ctx.TrendDirection = TradeDirection.Long;
                else if (ctx.Ema21Slope_M15 < 0)
                    ctx.TrendDirection = TradeDirection.Short;
            }

            // =================================================
            // PROFILES
            // =================================================
            FxInstrumentProfile fxProfile = isFx ? FxInstrumentMatrix.Get(SymbolRouting.NormalizeSymbol(symbol)) : null;
            IndexInstrumentProfile indexProfile =
                isIndex && IndexInstrumentMatrix.Contains(SymbolRouting.NormalizeSymbol(symbol))
                    ? IndexInstrumentMatrix.Get(SymbolRouting.NormalizeSymbol(symbol))
                    : null;

            XAU_InstrumentProfile metalProfile = null;

            if (isMetal && XAU_InstrumentMatrix.Contains(SymbolRouting.NormalizeSymbol(symbol)) )
            {
                metalProfile = XAU_InstrumentMatrix.Get(SymbolRouting.NormalizeSymbol(symbol));
            }

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

            double fallbackMult =
                isFx ? 0.55 :          // FX-en szigorúbb fallback
                isMetal ? 0.45 :       // ha ide esne (ritka), kicsit szigorúbb
                0.40;                  // teljesen generic

            if (isFx)
            {
                double mult = (fxProfile != null) ? fxProfile.ImpulseAtrMult_M5 : fallbackMult;

                ctx.HasImpulse_M5 =
                    ctx.AtrM5 > 0 &&
                    m5Body > ctx.AtrM5 * mult;
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
                    m5Body > ctx.AtrM5 * fallbackMult;
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
            // GENERIC 5-BAR MICRO STRUCTURE CHECK (USED BY CRYPTO/METAL/INDEX)
            // NOT THE SAME AS FX TUNED FLAG LOGIC
            // =================================================
            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = m5Idx - 4; i <= m5Idx; i++)
            {
                hi = Math.Max(hi, ctx.M5.HighPrices[i]);
                lo = Math.Min(lo, ctx.M5.LowPrices[i]);
            }

            double range5 = hi - lo;
            bool validRange =
                ctx.AtrM5 > 0 &&
                range5 > 0 &&
                range5 < ctx.AtrM5 * 3.0;

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

                double reference = ctx.M5.ClosePrices[m5Idx + 1];

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

            ctx.HasEarlyPullback_M5 =
                ctx.PullbackBars_M5 >= 1 &&
                ctx.PullbackDepthAtr_M5 >= 0.25;

            GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[PB] bars={ctx.PullbackBars_M5} depth={ctx.PullbackDepthAtr_M5:F2} early={ctx.HasEarlyPullback_M5}");

            // =================================================
            // FLAG FEATURE EXTRACTION (v2.22 – compression based, NOT pullback based)
            // =================================================

            // reset
            ctx.HasFlagLong_M5 = false;
            ctx.HasFlagShort_M5 = false;
            ctx.FlagHigh = 0;
            ctx.FlagLow = 0;
            ctx.FlagAtr_M5 = 0;

            // ---- lookback (adaptive alap később lehet instrument szerint)
            int lookback = 5;

            double flagHi = double.MinValue;
            double flagLo = double.MaxValue;

            // safety (index ne menjen ki)
            int flagLength = Math.Max(2, Math.Min(ctx.PullbackBars_M5, 5));

            if (flagLength < 2)
            {
                ctx.HasFlagLong_M5 = false;
                ctx.HasFlagShort_M5 = false;
            }

            int start = Math.Max(0, m5Idx - flagLength + 1);

            for (int i = start; i <= m5Idx; i++)
            {
                flagHi = Math.Max(flagHi, ctx.M5.HighPrices[i]);
                flagLo = Math.Min(flagLo, ctx.M5.LowPrices[i]);
            }

            int flagBars = m5Idx - start + 1;
            double flagRange = flagHi - flagLo;

            bool hasRecentImpulse =
                ctx.HasImpulse_M5 ||
                ctx.BarsSinceImpulse_M5 <= 3;

            bool validFlagBars =
                flagBars >= 2 &&
                flagBars <= 5;

            // ============================
            // 1) COMPRESSION
            // ============================

            double atrForFlag = ctx.AtrM5;

            double impulseRange = 0.0;

            int impulseIdx = m5Idx - ctx.BarsSinceImpulse_M5;

            if (ctx.BarsSinceImpulse_M5 >= 0 &&
                ctx.BarsSinceImpulse_M5 <= 3 &&
                ctx.BarsSinceImpulse_M5 < 999 &&
                impulseIdx >= 0)
            {
                impulseRange =
                    Math.Abs(ctx.M5.HighPrices[impulseIdx] - ctx.M5.LowPrices[impulseIdx]);
            }

            GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[IMPULSE SRC] idx={impulseIdx} barsSince={ctx.BarsSinceImpulse_M5} range={impulseRange:F2}");

            bool hasValidImpulseRange =
                impulseRange > 0 &&
                !double.IsNaN(impulseRange) &&
                !double.IsInfinity(impulseRange);

            double maxCompression =
                hasValidImpulseRange
                    ? Math.Min(0.8 * atrForFlag, 0.35 * impulseRange)
                    : 0.8 * atrForFlag;

            double maxRetrace =
                hasValidImpulseRange
                    ? Math.Min(0.6 * atrForFlag, 0.5 * impulseRange)
                    : 0.5 * atrForFlag;

            double retraceAmount =
                atrForFlag > 0
                    ? ctx.PullbackDepthAtr_M5 * atrForFlag
                    : 0.0;

            bool isTight =
                atrForFlag > 0 &&
                flagRange > 0 &&
                flagRange <= maxCompression;

            bool validRetrace =
                atrForFlag > 0
                    ? retraceAmount <= maxRetrace
                    : ctx.PullbackDepthAtr_M5 <= 0.5;

            // ============================
            // 2) MICRO STRUCTURE
            // ============================

            bool strongLowerHighs =
                ctx.M5.HighPrices[m5Idx] < ctx.M5.HighPrices[m5Idx - 2] &&
                ctx.M5.HighPrices[m5Idx - 2] < ctx.M5.HighPrices[m5Idx - 4];

            bool weakLowerHighs =
                ctx.M5.HighPrices[m5Idx] < ctx.M5.HighPrices[m5Idx - 1];

            bool hasLowerHighs = strongLowerHighs || weakLowerHighs;

            bool strongHigherLows =
                ctx.M5.LowPrices[m5Idx] > ctx.M5.LowPrices[m5Idx - 2] &&
                ctx.M5.LowPrices[m5Idx - 2] > ctx.M5.LowPrices[m5Idx - 4];

            bool weakHigherLows =
                ctx.M5.LowPrices[m5Idx] > ctx.M5.LowPrices[m5Idx - 1];

            bool hasHigherLows = strongHigherLows || weakHigherLows;

            // ============================
            // 3) DECELERATION (MÁR KISZÁMOLT)
            // ============================

            bool decelerating = ctx.IsPullbackDecelerating_M5;

            // ============================
            // 4) FINAL FLAG DECISION
            // ============================

            bool shortFlag =
                hasRecentImpulse &&
                validFlagBars &&
                isTight &&
                validRetrace &&
                hasLowerHighs &&
                decelerating;

            bool longFlag =
                hasRecentImpulse &&
                validFlagBars &&
                isTight &&
                validRetrace &&
                hasHigherLows &&
                decelerating;

            // ============================
            // 5) ASSIGN
            // ============================

            ctx.HasFlagShort_M5 = shortFlag;
            ctx.HasFlagLong_M5 = longFlag;

            // csak akkor mentjük a range-et ha EGYÉRTELMŰ
            if (shortFlag && !longFlag)
            {
                ctx.FlagHigh = flagHi;
                ctx.FlagLow = flagLo;
            }
            else if (longFlag && !shortFlag)
            {
                ctx.FlagHigh = flagHi;
                ctx.FlagLow = flagLo;
            }
            else
            {
                // konfliktus / nincs flag
                ctx.HasFlagShort_M5 = false;
                ctx.HasFlagLong_M5 = false;
            }

            if (!hasRecentImpulse)
            {
                ctx.HasFlagLong_M5 = false;
                ctx.HasFlagShort_M5 = false;
            }

            if (!validFlagBars)
            {
                ctx.HasFlagLong_M5 = false;
                ctx.HasFlagShort_M5 = false;
            }

            if (!validRetrace)
            {
                ctx.HasFlagLong_M5 = false;
                ctx.HasFlagShort_M5 = false;
            }

            GeminiV26.Core.Logging.GlobalLogger.Log(_bot,
                $"[FLAG FIX] recentImpulse={hasRecentImpulse} bars={flagBars} retraceOk={validRetrace} " +
                $"tight={isTight} decel={ctx.IsPullbackDecelerating_M5} " +
                $"long={ctx.HasFlagLong_M5} short={ctx.HasFlagShort_M5} " +
                $"impulse={impulseRange:F2} comp={flagRange:F2}/{maxCompression:F2} retr={retraceAmount:F2}/{maxRetrace:F2}"
            );

            // ATR-normalizált méret
            ctx.FlagAtr_M5 = flagRange;

            bool weakTrend =
                ctx.AtrM5 > 0 &&
                Math.Abs(ctx.Ema21Slope_M5) < ctx.AtrM5 * 0.15;

            bool compressed =
                ctx.AtrM5 > 0 &&
                ctx.FlagAtr_M5 > 0 &&
                ctx.FlagAtr_M5 < ctx.AtrM5 * 1.8;

            ctx.IsTransition_M5 =
                weakTrend || compressed;

            GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[REGIME] transition={ctx.IsTransition_M5} weakTrend={weakTrend} compressed={compressed}");

            // ============================
            // DEBUG (opcionális, de most hasznos)
            // ============================

            GeminiV26.Core.Logging.GlobalLogger.Log(_bot,
                $"[FLAG V2.22] tight={isTight} decel={decelerating} allowNoDecel=false " +
                $"LH={hasLowerHighs} HL={hasHigherLows} " +
                $"short={ctx.HasFlagShort_M5} long={ctx.HasFlagLong_M5} " +
                $"range={flagRange} atr={ctx.AtrM5}"
            );

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
            // M1 BREAKOUT (event-based, compression filtered)
            // =================================================

            double m1Hi = double.MinValue;
            double m1Lo = double.MaxValue;

            // előző 5 lezárt M1 bar
            for (int i = m1Idx - 5; i <= m1Idx - 1; i++)
            {
                m1Hi = Math.Max(m1Hi, ctx.M1.HighPrices[i]);
                m1Lo = Math.Min(m1Lo, ctx.M1.LowPrices[i]);
            }

            double prevClose = ctx.M1.ClosePrices[m1Idx - 1];
            double currClose = ctx.M1.ClosePrices[m1Idx];

            ctx.HasBreakout_M1 = false;
            ctx.BreakoutDirection = TradeDirection.None;

            // ============================
            // 1) EVENT-BASED CROSS
            // ============================

            bool crossUp = prevClose <= m1Hi && currClose > m1Hi;
            bool crossDown = prevClose >= m1Lo && currClose < m1Lo;

            // ============================
            // 2) COMPRESSION FILTER
            // ============================

            double m1Range = m1Hi - m1Lo;

            // instrument-aware compression
            double compressionMult =
                isCrypto ? 0.35 :
                isMetal  ? 0.50 :
                isFx     ? 0.40 :
                isIndex  ? 0.45 :
                           0.50;

            bool isCompressed =
                ctx.AtrM5 > 0 &&
                m1Range < ctx.AtrM5 * compressionMult;

            // ============================
            // FINAL DECISION
            // ============================

            if (isCompressed)
            {
                if (crossUp)
                {
                    ctx.HasBreakout_M1 = true;
                    ctx.BreakoutDirection = TradeDirection.Long;
                }
                else if (crossDown)
                {
                    ctx.HasBreakout_M1 = true;
                    ctx.BreakoutDirection = TradeDirection.Short;
                }
            }

            // trend alignment
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
            GeminiV26.Core.Logging.GlobalLogger.Log(_bot, $"[CTX][MEMORY_READY] symbol={symbol} hasMemory={ctx.HasMemory}");
            LogEntryMemorySnapshot(ctx, symbol);
            return ctx;
        }

        private void AttachMemorySnapshot(EntryContext ctx, string canonicalSymbol)
        {
            if (ctx == null)
                return;

            var state = _memoryEngine.GetState(canonicalSymbol);
            var assessment = _memoryEngine.GetAssessment(canonicalSymbol);

            ctx.MemoryState = state;
            ctx.MemoryResolved = state?.IsResolved == true;
            ctx.MemoryUsable = state?.IsUsable == true;
            ctx.MemoryAssessment = assessment;

            ctx.MemoryContinuationWindow = state?.ContinuationWindowState ?? ContinuationWindowState.Unknown;
            ctx.MemoryMoveExtension = state?.MoveExtensionState ?? MoveExtensionState.Unknown;
            ctx.MemoryImpulseFreshnessScore = state?.ImpulseFreshnessScore ?? 0;
            ctx.MemoryContinuationFreshnessScore = state?.ContinuationFreshnessScore ?? 0;
            ctx.MemoryTriggerLateScore = state?.TriggerLateScore ?? 0;
            ctx.MemoryTimingPenalty = assessment?.RecommendedTimingPenalty ?? 0;

            bool hasLongTiming = state?.ImpulseDirection > 0;
            bool hasShortTiming = state?.ImpulseDirection < 0;
            ctx.IsTimingLongActive = hasLongTiming;
            ctx.IsTimingShortActive = hasShortTiming;

            // Side-aware timing snapshot from directional memory impulse side.
            ctx.HasFreshPullbackLong = hasLongTiming && (assessment?.IsFirstPullbackWindow ?? false);
            ctx.HasFreshPullbackShort = hasShortTiming && (assessment?.IsFirstPullbackWindow ?? false);
            ctx.HasEarlyContinuationLong = hasLongTiming && (assessment?.IsEarlyContinuationWindow ?? false);
            ctx.HasEarlyContinuationShort = hasShortTiming && (assessment?.IsEarlyContinuationWindow ?? false);
            ctx.HasLateContinuationLong = hasLongTiming && (assessment?.IsLateContinuation ?? false);
            ctx.HasLateContinuationShort = hasShortTiming && (assessment?.IsLateContinuation ?? false);
            ctx.IsOverextendedLong = hasLongTiming && (assessment?.IsOverextendedMove ?? false);
            ctx.IsOverextendedShort = hasShortTiming && (assessment?.IsOverextendedMove ?? false);

            ctx.BarsSinceStructureBreakLong = hasLongTiming ? (state?.BarsSinceBreak ?? -1) : -1;
            ctx.BarsSinceStructureBreakShort = hasShortTiming ? (state?.BarsSinceBreak ?? -1) : -1;
            ctx.BarsSinceImpulseLong = hasLongTiming ? (state?.BarsSinceImpulse ?? -1) : -1;
            ctx.BarsSinceImpulseShort = hasShortTiming ? (state?.BarsSinceImpulse ?? -1) : -1;
            ctx.ContinuationAttemptCountLong = hasLongTiming ? (state?.ContinuationAttemptCount ?? 0) : -1;
            ctx.ContinuationAttemptCountShort = hasShortTiming ? (state?.ContinuationAttemptCount ?? 0) : -1;

            ctx.DistanceFromFastStructureAtrLong = hasLongTiming ? (state?.DistanceFromFastStructureAtr ?? 0) : 0;
            ctx.DistanceFromFastStructureAtrShort = hasShortTiming ? (state?.DistanceFromFastStructureAtr ?? 0) : 0;

            ctx.ContinuationFreshnessLong = hasLongTiming ? (state?.ContinuationFreshnessScore ?? 0) : 0;
            ctx.ContinuationFreshnessShort = hasShortTiming ? (state?.ContinuationFreshnessScore ?? 0) : 0;
            ctx.TriggerLateScoreLong = hasLongTiming ? (state?.TriggerLateScore ?? 0) : 0;
            ctx.TriggerLateScoreShort = hasShortTiming ? (state?.TriggerLateScore ?? 0) : 0;
        }

        private void LogEntryMemorySnapshot(EntryContext ctx, string symbol)
        {
            GeminiV26.Core.Logging.GlobalLogger.Log(_bot,
                $"[ENTRY][SNAPSHOT] symbol={symbol} movePhase={ctx?.MemoryState?.MovePhase ?? MovePhase.Unknown} continuationWindow={ctx?.MemoryContinuationWindow ?? ContinuationWindowState.Unknown} extensionState={ctx?.MemoryMoveExtension ?? MoveExtensionState.Unknown} impulseFreshness={ctx?.MemoryImpulseFreshnessScore ?? 0:0.00} continuationFreshness={ctx?.MemoryContinuationFreshnessScore ?? 0:0.00} triggerLateScore={ctx?.MemoryTriggerLateScore ?? 0:0.00} chaseRisk={ctx?.MemoryAssessment?.IsChaseRisk ?? false} timingPenalty={ctx?.MemoryTimingPenalty ?? 0}");

            GeminiV26.Core.Logging.GlobalLogger.Log(_bot,
                $"[CTX][TIMING][SIDE] symbol={symbol} side=LONG timingLongActive={ctx?.IsTimingLongActive ?? false} timingShortActive={ctx?.IsTimingShortActive ?? false} early={ctx?.HasEarlyContinuationLong ?? false} late={ctx?.HasLateContinuationLong ?? false} overextended={ctx?.IsOverextendedLong ?? false} freshness={ctx?.ContinuationFreshnessLong ?? 0:0.00} barsSinceImpulse={ctx?.BarsSinceImpulseLong ?? -1} barsSinceBreak={ctx?.BarsSinceStructureBreakLong ?? -1} attempts={ctx?.ContinuationAttemptCountLong ?? -1} triggerLate={ctx?.TriggerLateScoreLong ?? 0:0.00} distanceAtr={ctx?.DistanceFromFastStructureAtrLong ?? 0:0.00}");
            GeminiV26.Core.Logging.GlobalLogger.Log(_bot,
                $"[CTX][TIMING][SIDE] symbol={symbol} side=SHORT timingLongActive={ctx?.IsTimingLongActive ?? false} timingShortActive={ctx?.IsTimingShortActive ?? false} early={ctx?.HasEarlyContinuationShort ?? false} late={ctx?.HasLateContinuationShort ?? false} overextended={ctx?.IsOverextendedShort ?? false} freshness={ctx?.ContinuationFreshnessShort ?? 0:0.00} barsSinceImpulse={ctx?.BarsSinceImpulseShort ?? -1} barsSinceBreak={ctx?.BarsSinceStructureBreakShort ?? -1} attempts={ctx?.ContinuationAttemptCountShort ?? -1} triggerLate={ctx?.TriggerLateScoreShort ?? 0:0.00} distanceAtr={ctx?.DistanceFromFastStructureAtrShort ?? 0:0.00}");
        }

        private string CreateEntryAttemptId(string symbol)
        {
            string cleanSymbol = string.IsNullOrWhiteSpace(symbol)
                ? "SYM"
                : symbol.Trim().ToUpperInvariant();

            string head = cleanSymbol.Length <= 3
                ? cleanSymbol
                : cleanSymbol.Substring(0, 3);

            long millis = _bot.Server.Time.Ticks / TimeSpan.TicksPerMillisecond;
            string suffix = ToBase36(Math.Abs(millis % 2176782336L)).PadLeft(6, '0');
            return $"{head}{suffix}";
        }

        private static string ToBase36(long value)
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (value <= 0)
                return "0";

            char[] buffer = new char[16];
            int index = buffer.Length;

            while (value > 0)
            {
                buffer[--index] = alphabet[(int)(value % 36)];
                value /= 36;
            }

            return new string(buffer, index, buffer.Length - index);
        }
    }
}
