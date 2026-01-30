using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Flag;

        // ===== Tunables (INDEX) =====
        private const int MaxBarsSinceImpulse = 4;
        private const int FlagBars = 3;

        // flag legyen szűk
        private const double MaxFlagRangeAtr = 1.2;

        // breakout close buffer
        private const double BreakBufferAtr = 0.06;

        // ne legyen túl nyújtva EMA21-től
        private const double MaxDistFromEmaAtr = 0.90;

        // extra valóság gate-ek
        private const double MaxBreakoutBodyToRangeMin = 0.45; // breakout body min 45% range
        private const double MaxFlagSlopeAtr = 0.35;           // flag "drift" tilt (range tetején sodródás)

        private const int BaseScore = 85;
        private const int MinScore = 75;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 30)
                return Invalid(ctx, "CTX_NOT_READY");

            // =====================================================
            // PROFILE (KIEGÉSZÍTÉS – NEM MÓDOSÍTÁS)
            // =====================================================
            var p = IndexInstrumentMatrix.Get(ctx.Symbol);

            int maxBarsSinceImpulse =
                p.MaxBarsSinceImpulse_M5 > 0 ? p.MaxBarsSinceImpulse_M5 : MaxBarsSinceImpulse;

            int flagBars =
                p.FlagBars > 0 ? p.FlagBars : FlagBars;

            double maxFlagRangeAtr =
                p.MaxFlagAtrMult > 0 ? p.MaxFlagAtrMult : MaxFlagRangeAtr;

            double breakoutBufferAtr =
                p.BreakoutBufferAtr > 0 ? p.BreakoutBufferAtr : BreakBufferAtr;

            double maxDistFromEmaAtr =
                p.MaxEmaDistanceAtr > 0 ? p.MaxEmaDistanceAtr : MaxDistFromEmaAtr;
            // =====================================================

            var bars = ctx.M5;
            var dir = ctx.TrendDirection;
            int score = BaseScore;

            if (dir == TradeDirection.None)
                return Invalid(ctx, "NO_TREND_DIR");

            // ===== MarketState SOFT (INDEX) =====
            if (ctx.MarketState?.IsLowVol == true)
                score -= 15;

            if (ctx.MarketState?.IsTrend != true)
                score -= 10;

            // ===== Impulse gate =====
            if (!ctx.HasImpulse_M5)
            {
                if (ctx.MarketState != null && ctx.MarketState.IsTrend)
                    score -= 12;
                else
                    return Invalid(ctx, "NO_IMPULSE");
            }

            if (ctx.BarsSinceImpulse_M5 > maxBarsSinceImpulse)
            {
                if (ctx.MarketState != null && ctx.MarketState.IsTrend)
                    score -= 8;
                else
                    return Invalid(ctx, "STALE_IMPULSE");
            }

            // ===== (1) Continuation structure – SOFT (INDEX) =====
            if (dir == TradeDirection.Long)
            {
                if (ctx.BrokeLastSwingHigh_M5)
                    score += 8;
                else
                {
                    if (ctx.M5.Last(1).Close > ctx.Ema21_M5 && ctx.IsValidFlagStructure_M5)
                        score += 2;
                    else
                        score -= 10;
                }
            }
            else if (dir == TradeDirection.Short)
            {
                if (ctx.BrokeLastSwingLow_M5)
                    score += 8;
                else
                {
                    if (ctx.M5.Last(1).Close < ctx.Ema21_M5 && ctx.IsValidFlagStructure_M5)
                        score += 2;
                    else
                        score -= 10;
                }
            }

            // ===== Flag structure signal =====
            if (!ctx.IsValidFlagStructure_M5)
                return Invalid(ctx, "NO_FLAG_STRUCTURE");

            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;
            int flagStart = flagEnd - flagBars + 1;

            if (flagStart < 2)
                return Invalid(ctx, "NOT_ENOUGH_FLAG_BARS");

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_ZERO");

            // ===== Flag range =====
            double hi = double.MinValue, lo = double.MaxValue;
            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = System.Math.Max(hi, bars[i].High);
                lo = System.Math.Min(lo, bars[i].Low);
            }

            double flagRange = hi - lo;
            double maxFlag = ctx.AtrM5 * maxFlagRangeAtr;

            if (flagRange > maxFlag)
            {
                if (flagRange <= maxFlag * 1.25)
                    score -= 8;
                else
                    return Invalid(ctx, "FLAG_TOO_WIDE");
            }

            // ===== (2) Distribution / drift tilt =====
            double netMove = System.Math.Abs(bars[flagEnd].Close - bars[flagStart].Open);
            if (netMove < ctx.AtrM5 * MaxFlagSlopeAtr)
                score -= 8;

            // ===== Location gate =====
            double close = bars[lastClosed].Close;
            double distFromEma = System.Math.Abs(close - ctx.Ema21_M5);
            if (distFromEma > ctx.AtrM5 * maxDistFromEmaAtr)
                score -= 10;

            // ===== Breakout CLOSE kötelező =====
            double buf = ctx.AtrM5 * breakoutBufferAtr;
            bool bullBreak = close > hi + buf;
            bool bearBreak = close < lo - buf;

            if (dir == TradeDirection.Long && !bullBreak)
                return Invalid(ctx, "NO_BREAKOUT_CLOSE_LONG");
            if (dir == TradeDirection.Short && !bearBreak)
                return Invalid(ctx, "NO_BREAKOUT_CLOSE_SHORT");

            // ===== Breakout candle minőség =====
            double o = bars[lastClosed].Open;
            double h = bars[lastClosed].High;
            double l = bars[lastClosed].Low;

            double range = h - l;
            if (range <= 0)
                return Invalid(ctx, "BAD_BAR_RANGE");

            double body = System.Math.Abs(close - o);
            double bodyRatio = body / range;

            if (bodyRatio < MaxBreakoutBodyToRangeMin)
                return Invalid(ctx, "WEAK_BREAKOUT_BODY");

            if (dir == TradeDirection.Long && close <= o)
                return Invalid(ctx, "NO_BULL_BODY");
            if (dir == TradeDirection.Short && close >= o)
                return Invalid(ctx, "NO_BEAR_BODY");

            // ===== SCORE =====
            if (ctx.M1TriggerInTrendDirection)
                score += 5;

            if (ctx.IsAtrExpanding_M5)
                score += 2;

            if (score < MinScore)
                return Invalid(ctx, $"LOW_SCORE({score})");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_TRUEFLAG_PRO dir={dir} score={score} " +
                    $"flagATR={(flagRange / ctx.AtrM5):F2} bodyR={bodyRatio:F2}"
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason) =>
            new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
    }
}
