using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Pullback;

        private const int BaseScore = 55;

        // ===== FALLBACK LIMITS (MEGMARADNAK) =====
        private const double MaxPullbackDepthAtr = 0.8;
        private const int MaxPullbackBars = 4;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (!ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 5)
                return null;

            // =====================================================
            // PROFILE (KIEGÉSZÍTÉS – NEM MÓDOSÍTÁS)
            // =====================================================
            var p = IndexInstrumentMatrix.Get(ctx.Symbol);

            double maxPullbackDepthAtr =
                p.PullbackStyle == IndexPullbackStyle.Shallow ? 0.7 :
                p.PullbackStyle == IndexPullbackStyle.Structure ? 0.9 :
                MaxPullbackDepthAtr;

            int maxPullbackBars =
                p.PullbackStyle == IndexPullbackStyle.Shallow ? 3 :
                p.PullbackStyle == IndexPullbackStyle.Structure ? 5 :
                MaxPullbackBars;
            // =====================================================

            TradeDirection dir = ctx.TrendDirection;
            if (dir == TradeDirection.None)
                return null;

            // pullback alatt nem lehet impulse
            if (ctx.IsAtrExpanding_M5)
                return null;

            // shallow pullback (PROFILE-DRIVEN)
            if (ctx.PullbackDepthAtr_M5 <= 0 || ctx.PullbackDepthAtr_M5 > maxPullbackDepthAtr)
                return null;

            if (ctx.PullbackBars_M5 > maxPullbackBars)
                return null;

            if (!ctx.HasReactionCandle_M5)
                return null;

            int score = BaseScore;

            // soft context
            if (ctx.M1TriggerInTrendDirection)
                score += 10;

            if (ctx.MarketState?.IsTrend == true)
                score += 5;

            // if flag structure is forming, let FlagEntry win
            if (ctx.IsValidFlagStructure_M5)
                return null;

            if (score < 50)
                return null;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_PULLBACK_3.8 dir={dir} score={score} " +
                    $"pbATR={ctx.PullbackDepthAtr_M5:F2} pbBars={ctx.PullbackBars_M5}"
            };
        }
    }
}
