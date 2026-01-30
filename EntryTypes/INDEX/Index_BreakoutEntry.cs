using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_BreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Breakout;

        private const int BaseScore = 50;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (!ctx.IsReady)
                return null;

            // =====================================================
            // PROFILE (KIEGÉSZÍTÉS – NEM MÓDOSÍTÁS)
            // =====================================================
            var p = IndexInstrumentMatrix.Get(ctx.Symbol);
            // =====================================================

            if (!ctx.HasBreakout_M1)
                return null;

            TradeDirection dir = ctx.BreakoutDirection;
            if (dir == TradeDirection.None)
                return null;

            int score = BaseScore;

            if (ctx.TrendDirection == dir)
                score += 5;

            if (ctx.HasImpulse_M5)
                score += 5;

            if (ctx.IsAtrExpanding_M5)
                score += 3;

            // market state = SOFT
            if (ctx.MarketState?.IsTrend == true)
                score += 5;

            if (ctx.MarketState?.IsLowVol == true)
                score -= 15;

            // =====================================================
            // PROFILE SOFT BIAS (INDEX-SAFE)
            // =====================================================
            if (p.SessionBias == IndexSessionBias.NewYork)
                score += 1;

            if (p.Volatility == IndexVolatilityClass.Extreme)
                score += 1;
            // =====================================================

            // breakout NEVER beats flag/pullback by score
            score -= 10;

            if (score < 55)
                return null;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_BREAKOUT_3.8 dir={dir} score={score} " +
                    $"trendAlign={(ctx.TrendDirection == dir)} impulse={ctx.HasImpulse_M5}"
            };
        }
    }
}
