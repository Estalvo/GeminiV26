using cAlgo.API;
using GeminiV26.Core.Entry;
using System;
using System.Collections.Generic;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Flag;

        private const int MaxBarsSinceImpulse = 14;
        private const int MinFlagBars = 3;
        private const int MaxFlagBars = 7;

        private const double BreakBufferAtr = 0.03;
        private const double MaxDistFromEmaAtr = 1.2;

        private const int MinScore = 16;
        private const int HtfAgainstPenalty = 8;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_ZERO");

            if (!ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > 12)
                return Invalid(ctx, "NO_RECENT_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > MaxBarsSinceImpulse)
                return Invalid(ctx, "LATE_FLAG");

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;

            // =============================
            // Dynamic window (tightest)
            // =============================
            int bestStart = -1;            
            double bestRange = double.MaxValue;

            for (int len = MinFlagBars; len <= MaxFlagBars; len++)
            {
                int start = flagEnd - len + 1;
                if (start < 2)
                    continue;

                double hiTmp = double.MinValue;
                double loTmp = double.MaxValue;

                for (int i = start; i <= flagEnd; i++)
                {
                    double bodyHigh = Math.Max(bars[i].Open, bars[i].Close);
                    double bodyLow  = Math.Min(bars[i].Open, bars[i].Close);

                    hiTmp = Math.Max(hiTmp, bodyHigh);
                    loTmp = Math.Min(loTmp, bodyLow);
                }

                double r = hiTmp - loTmp;

                if (r < bestRange)
                {
                    bestRange = r;
                    bestStart = start;
                }
            }

            if (bestStart < 2)
                return Invalid(ctx, "NO_FLAG_WINDOW");

            int flagStart = bestStart;
            double range = bestRange;

            double rangeAtr = range / ctx.AtrM5;

            // width guard
            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);
            if (rangeAtr < 0.15)
                return Invalid(ctx, "FLAG_TOO_TIGHT");
    
            if (profile != null && rangeAtr > profile.MaxFlagAtrMult)
                return Invalid(ctx, "FLAG_TOO_WIDE");

            
            // =============================
            // Evaluate BOTH directions
            // =============================
            var longEval = EvaluateSide(ctx, TradeDirection.Long, hi, lo, rangeAtr, lastClosed);
            var shortEval = EvaluateSide(ctx, TradeDirection.Short, hi, lo, rangeAtr, lastClosed);

            bool buyValid = longEval.IsValid;
            bool sellValid = shortEval.IsValid;

            if (!buyValid && !sellValid)
            {
                ctx.Log?.Invoke($"[FLAG][REJECT] No valid direction buyValid={buyValid} sellValid={sellValid}");
                return Invalid(ctx, "FLAG_DIRECTION_INVALID");
            }

            if (buyValid && !sellValid) return longEval;
            if (!buyValid && sellValid) return shortEval;

            return longEval.Score >= shortEval.Score ? longEval : shortEval;
        }

        private EntryEvaluation EvaluateSide(
            EntryContext ctx,
            TradeDirection dir,
            double hi,
            double lo,
            double rangeAtr,
            int lastIndex)
        {
            var bars = ctx.M5;
            var bar = bars[lastIndex];

            int score = 0;

            // =============================
            // Compression (soft)
            // =============================
            bool compression =
                ctx.AtrSlope_M5 <= 0.30 &&
                ctx.AdxSlope_M5 <= 1.5;

            if (!compression)
                score -= 6;

            // =============================
            // Volatility
            // =============================
            if (ctx.IsVolatilityAcceptable_Crypto)
                score += 10;
            else
                score -= 10;

            // =============================
            // EMA distance
            // =============================
            double close = bar.Close;
            double open = bar.Open;

            double distFromEma = Math.Abs(close - ctx.Ema21_M5);

            if (distFromEma <= ctx.AtrM5 * 0.6)
                score += 8;
            else if (distFromEma > ctx.AtrM5 * MaxDistFromEmaAtr)
                score -= 8;

            // =============================
            // Breakout logic
            // =============================
            double buf = ctx.AtrM5 * BreakBufferAtr;

            bool bullBreak = close > hi + buf;
            bool bearBreak = close < lo - buf;

            // --- NEW: reclaim after breakout ---
            bool bullReclaim =
                close > hi &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.HasReactionCandle_M5;

            bool bearReclaim =
                close < lo &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.HasReactionCandle_M5;

            bool longValid = bullBreak || bullReclaim;
            bool shortValid = bearBreak || bearReclaim;

            if (dir == TradeDirection.Long && !longValid)
                return Invalid(ctx, "NO_BREAK_LONG");

            if (dir == TradeDirection.Short && !shortValid)
                return Invalid(ctx, "NO_BREAK_SHORT");
                
            // Candle quality
            if (dir == TradeDirection.Long && close > open) score += 6;
            if (dir == TradeDirection.Short && close < open) score += 6;

            // =============================
            // HTF soft penalty
            // =============================
            if (ctx.TrendDirection != TradeDirection.None &&
                dir != ctx.TrendDirection)
            {
                score -= HtfAgainstPenalty;
            }

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            if (missingImpulse)
            {
                score = score; //Math.Max(0, score - 6); csak 7végére 0!

                ctx.Log?.Invoke(
                    "[FLAG][PENALTY] Missing impulse detected → score penalty applied " +
                    $"symbol={ctx.Symbol} entry={EntryType.Crypto_Flag} penalty=6 score={score}");
            }

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CR_FLAG_V2 dir={dir} score={score} rangeATR={rangeAtr:F2}"
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
    }
}
