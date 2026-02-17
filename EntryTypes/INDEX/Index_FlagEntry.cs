using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Flag;

        private const int MaxBarsSinceImpulse = 4;
        private const int FlagBars = 3;

        private const double MaxFlagRangeAtr = 1.2;
        private const double BreakBufferAtr = 0.06;
        private const double MaxDistFromEmaAtr = 0.90;

        private const double MaxBreakoutBodyToRangeMin = 0.45;
        private const double MaxFlagSlopeAtr = 0.35;

        private const int BaseScore = 85;
        private const int MinScore = 70;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 0;

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 30)
                return Reject(ctx, "CTX_NOT_READY", score, TradeDirection.None);

            var p = IndexInstrumentMatrix.Get(ctx.Symbol);

            int maxBarsSinceImpulse = p.MaxBarsSinceImpulse_M5 > 0 ? p.MaxBarsSinceImpulse_M5 : MaxBarsSinceImpulse;
            int flagBars = p.FlagBars > 0 ? p.FlagBars : FlagBars;
            double maxFlagRangeAtr = p.MaxFlagAtrMult > 0 ? p.MaxFlagAtrMult : MaxFlagRangeAtr;
            double breakoutBufferAtr = p.BreakoutBufferAtr > 0 ? p.BreakoutBufferAtr : BreakBufferAtr;
            double maxDistFromEmaAtr = p.MaxEmaDistanceAtr > 0 ? p.MaxEmaDistanceAtr : MaxDistFromEmaAtr;

            bool requireStructure = (p.PullbackStyle == IndexPullbackStyle.Structure);

            double minAdxTrend = p.MinAdxTrend > 0 ? p.MinAdxTrend : 20;
            double chopAdxThreshold = p.ChopAdxThreshold > 0 ? p.ChopAdxThreshold : minAdxTrend;
            double chopDiDiff = p.ChopDiDiffThreshold > 0 ? p.ChopDiDiffThreshold : 7;

            int fatigueThreshold = p.FatigueThreshold > 0 ? p.FatigueThreshold : 3;
            double fatigueAdxLevel = p.FatigueAdxLevel > 0 ? p.FatigueAdxLevel : 40;

            double scoreMultiplier = p.ScoreWeightMultiplier > 0 ? p.ScoreWeightMultiplier : 1.0;

            Console.WriteLine(
                $"[IDX_FLAG][PROFILE] sym={ctx.Symbol} norm={p.Symbol} " +
                $"minAdx={minAdxTrend} chopAdx={chopAdxThreshold} fatigueTh={fatigueThreshold} " +
                $"scoreMult={scoreMultiplier:F2}"
            );

            var bars = ctx.M5;
            var dir = ctx.TrendDirection;

            if (dir == TradeDirection.None)
                return Reject(ctx, "NO_TREND_DIR", score, TradeDirection.None);

            score = BaseScore;

            // =========================
            // MARKET STATE
            // =========================
            if (ctx.MarketState?.IsLowVol == true)
                score -= 8;

            if (ctx.MarketState?.IsTrend != true)
                score -= 6;

            // =========================
            // CHOP
            // =========================
            bool chopZone =
                ctx.Adx_M5 < chopAdxThreshold &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < chopDiDiff &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                score -= 6;

            // =========================
            // IMPULSE GATE
            // =========================
            if (!ctx.HasImpulse_M5)
            {
                if (ctx.MarketState?.IsTrend == true)
                    score -= 8;
                else
                    return Reject(ctx, "NO_IMPULSE", score, dir);
            }

            if (ctx.BarsSinceImpulse_M5 > maxBarsSinceImpulse)
            {
                if (ctx.MarketState?.IsTrend == true)
                    score -= 6;
                else
                    return Reject(ctx, "STALE_IMPULSE", score, dir);
            }

            // =========================
            // MATRIX-DRIVEN FATIGUE
            // =========================
            bool adxExhausted = ctx.Adx_M5 > fatigueAdxLevel && ctx.AdxSlope_M5 <= 0;
            bool atrContracting = ctx.AtrSlope_M5 <= 0;
            bool diConverging = Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < chopDiDiff;
            bool impulseStale = !ctx.HasImpulse_M5 || ctx.BarsSinceImpulse_M5 > maxBarsSinceImpulse;

            int fatigueCount = 0;
            if (adxExhausted) fatigueCount++;
            if (atrContracting) fatigueCount++;
            if (diConverging) fatigueCount++;
            if (impulseStale) fatigueCount++;

            if (fatigueCount >= fatigueThreshold)
                return Reject(ctx, "IDX_TREND_FATIGUE_ULTRASOUND", score, dir);

            // =========================
            // CONTINUATION STRUCTURE
            // =========================
            if (dir == TradeDirection.Long)
            {
                if (ctx.BrokeLastSwingHigh_M5)
                    score += 8;
                else if (!requireStructure && ctx.M5.Last(1).Close > ctx.Ema21_M5 && ctx.IsValidFlagStructure_M5)
                    score += 2;
                else
                    score -= 10;
            }
            else
            {
                if (ctx.BrokeLastSwingLow_M5)
                    score += 8;
                else if (!requireStructure && ctx.M5.Last(1).Close < ctx.Ema21_M5 && ctx.IsValidFlagStructure_M5)
                    score += 2;
                else
                    score -= 10;
            }

            if (!ctx.IsValidFlagStructure_M5)
                score -= 8;

            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;
            int flagStart = flagEnd - flagBars + 1;

            if (flagStart < 2)
                return Reject(ctx, "NOT_ENOUGH_FLAG_BARS", score, dir);

            if (ctx.AtrM5 <= 0)
                return Reject(ctx, "ATR_ZERO", score, dir);

            double hi = double.MinValue, lo = double.MaxValue;
            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            double flagRange = hi - lo;
            double maxFlag = ctx.AtrM5 * maxFlagRangeAtr;

            if (flagRange > maxFlag)
            {
                if (flagRange <= maxFlag * 1.25)
                    score -= 8;
                else
                    return Reject(ctx, "FLAG_TOO_WIDE", score, dir);
            }

            double netMove = Math.Abs(bars[flagEnd].Close - bars[flagStart].Open);
            if (netMove < ctx.AtrM5 * MaxFlagSlopeAtr)
                score -= 8;

            double close = bars[lastClosed].Close;
            double distFromEma = Math.Abs(close - ctx.Ema21_M5);
            if (distFromEma > ctx.AtrM5 * maxDistFromEmaAtr)
                score -= 6;

            double buf = ctx.AtrM5 * breakoutBufferAtr;
            bool bullBreak = close > hi + buf;
            bool bearBreak = close < lo - buf;

            if (dir == TradeDirection.Long && !bullBreak)
                score -= 10;

            if (dir == TradeDirection.Short && !bearBreak)
                score -= 10;

            double o = bars[lastClosed].Open;
            double h = bars[lastClosed].High;
            double l = bars[lastClosed].Low;

            double range = h - l;
            if (range <= 0)
                return Reject(ctx, "BAD_BAR_RANGE", score, dir);

            double body = Math.Abs(close - o);
            double bodyRatio = body / range;

            if (bodyRatio < MaxBreakoutBodyToRangeMin)
                score -= 8;

            if (dir == TradeDirection.Long && close <= o)
                score -= 8;

            if (dir == TradeDirection.Short && close >= o)
                score -= 8;

            if (ctx.M1TriggerInTrendDirection)
                score += 5;

            if (ctx.IsAtrExpanding_M5)
                score += 2;

            // =========================
            // MATRIX SCORE MULTIPLIER
            // =========================
            score = (int)Math.Round(score * scoreMultiplier);

            if (score < MinScore)
                return Reject(ctx, $"LOW_SCORE({score})", score, dir);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_FLAG dir={dir} score={score} mult={scoreMultiplier:F2} " +
                    $"fatigue={fatigueCount}/{fatigueThreshold} " +
                    $"flagATR={(flagRange / ctx.AtrM5):F2}"
            };
        }

        private static EntryEvaluation Reject(
            EntryContext ctx,
            string reason,
            int score,
            TradeDirection dir)
        {
            Console.WriteLine(
                $"[IDX_FLAG][REJECT] {reason} | score={score} | dir={dir} | " +
                $"ADX={ctx?.Adx_M5:F1} Impulse={ctx?.HasImpulse_M5}"
            );

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Flag,
                Direction = dir,
                IsValid = false,
                Score = Math.Max(0, score),
                Reason = reason
            };
        }
    }
}
