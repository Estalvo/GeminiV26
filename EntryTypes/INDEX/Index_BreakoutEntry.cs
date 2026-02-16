using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_BreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Breakout;

        private const int BaseScore = 55;
        private const int MinScore = 55;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 0;

            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, "CTX_NOT_READY", score, TradeDirection.None);

            var p = IndexInstrumentMatrix.Get(ctx.Symbol);

            if (!ctx.HasBreakout_M1)
                return Reject(ctx, "NO_BREAKOUT_M1", score, TradeDirection.None);

            TradeDirection dir = ctx.BreakoutDirection;
            if (dir == TradeDirection.None)
                return Reject(ctx, "NO_BREAKOUT_DIR", score, TradeDirection.None);

            score = BaseScore;

            // =====================================================
            // Chop / Range SOFT
            // =====================================================
            bool chopZone =
                ctx.Adx_M5 < p.MinAdxTrend &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 8 &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                score -= 6;

            // =====================================================
            // Trend Fatigue Ultrasound (HARD REJECT)
            // =====================================================
            bool adxExhausted = ctx.Adx_M5 > 40 && ctx.AdxSlope_M5 <= 0;
            bool atrContracting = ctx.AtrSlope_M5 <= 0;
            bool diConverging = System.Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7;
            bool impulseStale = !ctx.HasImpulse_M5 || ctx.BarsSinceImpulse_M5 > 3;

            int fatigueCount = 0;
            if (adxExhausted) fatigueCount++;
            if (atrContracting) fatigueCount++;
            if (diConverging) fatigueCount++;
            if (impulseStale) fatigueCount++;

            if (fatigueCount >= 3)
                return Reject(ctx, "IDX_TREND_FATIGUE_ULTRASOUND", score, dir);

            // =====================================================
            // Core breakout scoring
            // =====================================================
            if (ctx.TrendDirection == dir)
                score += 5;

            if (ctx.HasImpulse_M5)
                score += 5;

            if (ctx.IsAtrExpanding_M5)
                score += 3;

            if (ctx.MarketState?.IsTrend == true)
                score += 5;

            if (ctx.MarketState?.IsLowVol == true)
                score -= 8; // was -15

            // Strong aligned momentum bonus
            if (ctx.TrendDirection == dir &&
                ctx.HasImpulse_M5 &&
                ctx.IsAtrExpanding_M5)
            {
                score += 3;
            }

            // =====================================================
            // Fake breakout guard (HARD)
            // =====================================================
            bool weakBreakout =
                !ctx.IsAtrExpanding_M5 &&
                !ctx.HasImpulse_M5 &&
                ctx.TrendDirection != dir &&
                ctx.Adx_M5 < p.MinAdxTrend;

            if (weakBreakout)
                return Reject(ctx, "WEAK_FAKE_BREAKOUT", score, dir);

            // =====================================================
            // Profile soft bias
            // =====================================================
            if (p.SessionBias == IndexSessionBias.NewYork)
                score += 1;

            if (p.Volatility == IndexVolatilityClass.Extreme)
                score += 1;

            // Breakout shouldn't beat Flag/Pullback too easily
            if (p.PullbackStyle == IndexPullbackStyle.Structure)
                score -= 6;
            else
                score -= 4;

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
                    $"IDX_BREAKOUT dir={dir} score={score} " +
                    $"trendAlign={(ctx.TrendDirection == dir)} impulse={ctx.HasImpulse_M5} atrExp={ctx.IsAtrExpanding_M5}"
            };
        }

        private static EntryEvaluation Reject(
            EntryContext ctx,
            string reason,
            int score,
            TradeDirection dir)
        {
            Console.WriteLine(
                $"[IDX_FLAG][REJECT] {reason} | " +
                $"score={score} | " +
                $"dir={dir} | " +
                $"Impulse={ctx?.HasImpulse_M5} | " +
                $"BarsSinceImp={ctx?.BarsSinceImpulse_M5} | " +
                $"ATRexp={ctx?.IsAtrExpanding_M5} | " +
                $"ADX={ctx?.Adx_M5:F1} | " +
                $"DIΔ={System.Math.Abs((ctx?.PlusDI_M5 ?? 0) - (ctx?.MinusDI_M5 ?? 0)):F1}"
            );

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Breakout,
                Direction = dir,
                IsValid = false,
                Score = System.Math.Max(0, score),
                Reason = reason
            };
        }
    }
}
