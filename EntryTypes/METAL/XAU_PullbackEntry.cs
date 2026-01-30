using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Pullback;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, "CTX_NOT_READY");

            // =========================
            // HARD MARKET STATE GATES (XAU)
            // =========================
            // XAU pullback csak VALÓDI trendben
            if (ctx.MarketState == null || !ctx.MarketState.IsTrend)
                return Reject(ctx, "XAU_NO_TREND_STATE");

            // ADX túl alacsony → nincs follow-through
            if (ctx.MarketState.Adx < 16.0)
                return Reject(ctx, "XAU_ADX_TOO_LOW");

            // =========================
            // DIRECTION (XAU – TREND ONLY)
            // =========================
            TradeDirection dir = ctx.TrendDirection;

            if (dir != TradeDirection.Long && dir != TradeDirection.Short)
                return Reject(ctx, "NO_TREND_DIR");

            int score = 60;

            // =========================
            // TIME MEMORY GATES (XAU)
            // =========================

            // túl friss impulzus → SOFT
            if (ctx.BarsSinceImpulse_M5 < 1)
                score -= 10;

            // túl régi impulzus → HARD
            if (ctx.BarsSinceImpulse_M5 > 6)
                return Reject(ctx, "STALE_IMPULSE");

            // pullback ne húzódjon el
            if (ctx.PullbackBars_M5 > 3)
                return Reject(ctx, "PULLBACK_TOO_LONG");

            // =========================
            // VOLATILITY SPIKE FILTER (XAU)
            // =========================
            if (ctx.BarsSinceImpulse_M5 <= 1 && ctx.IsAtrExpanding_M5)
                score -= 10;

            // =========================
            // IMPULSE REQUIREMENT
            // =========================
            if (!ctx.HasImpulse_M5 && !ctx.IsAtrExpanding_M5)
                return Reject(ctx, "NO_IMPULSE");

            score += 10;

            // =========================
            // PULLBACK QUALITY
            // =========================

            // pullback ne legyen túl mély
            if (ctx.PullbackDepthAtr_M5 > 1.8)
                return Reject(ctx, "PULLBACK_TOO_DEEP");

            score += 10;

            // =========================
            // M1 TRIGGER
            // =========================
            if (ctx.M1TriggerInTrendDirection)
                score += 10;

            // =========================
            // DYNAMIC MIN SCORE (XAU)
            // =========================
            int minScore = 35;

            if (ctx.PullbackDepthAtr_M5 > 1.2)
                minScore += 5;

            if (ctx.BarsSinceImpulse_M5 >= 4)
                minScore += 5;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = score >= minScore,
                Reason = $"XAU_PB score={score}/{minScore}"
            };
        }

        private EntryEvaluation Reject(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
        }
    }
}
