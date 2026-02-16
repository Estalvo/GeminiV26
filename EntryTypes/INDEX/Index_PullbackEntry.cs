using GeminiV26.Core.Entry;
using GeminiV26.Instruments.INDEX;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Pullback;

        private const int BaseScore = 60;   // ⬆ emelve (55 → 60)
        private const int MinScore = 55;    // ⬆ enyhén emelve (52 → 55)

        // ===== FALLBACK LIMITS =====
        private const double MaxPullbackDepthAtr = 0.9;   // 0.8 → 0.9 (index bírja)
        private const int MaxPullbackBars = 5;            // 4 → 5 (ne ölje meg a jó retrace-t)

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (!ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 10)
                return null;

            var p = IndexInstrumentMatrix.Get(ctx.Symbol);

            double maxPullbackDepthAtr =
                p.PullbackStyle == IndexPullbackStyle.Shallow ? 0.7 :
                p.PullbackStyle == IndexPullbackStyle.Structure ? 1.0 :
                MaxPullbackDepthAtr;

            int maxPullbackBars =
                p.PullbackStyle == IndexPullbackStyle.Shallow ? 3 :
                p.PullbackStyle == IndexPullbackStyle.Structure ? 6 :
                MaxPullbackBars;

            TradeDirection dir = ctx.TrendDirection;
            if (dir == TradeDirection.None)
                return null;

            int score = BaseScore;

            // =====================================================
            // CHOP SOFT
            // =====================================================
            bool chopZone =
                ctx.Adx_M5 < 20 &&
                System.Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7 &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                score -= 6;

            // =====================================================
            // TREND FATIGUE → SOFT (nem hard kill)
            // =====================================================
            bool adxExhausted =
                ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0;

            bool atrContracting =
                ctx.AtrSlope_M5 <= 0;

            bool diConverging =
                System.Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7;

            bool impulseStale =
                !ctx.HasImpulse_M5 || ctx.BarsSinceImpulse_M5 > 4;

            int fatigueCount = 0;
            if (adxExhausted) fatigueCount++;
            if (atrContracting) fatigueCount++;
            if (diConverging) fatigueCount++;
            if (impulseStale) fatigueCount++;

            bool trendFatigue = fatigueCount >= 3;

            if (trendFatigue)
                score -= 12;

            // =====================================================
            // PULLBACK STRUCTURAL GATES
            // =====================================================

            // Pullback alatt ATR ne expandáljon erősen
            if (ctx.IsAtrExpanding_M5 && ctx.PullbackDepthAtr_M5 > 0.6)
                score -= 6;

            if (ctx.PullbackDepthAtr_M5 <= 0 ||
                ctx.PullbackDepthAtr_M5 > maxPullbackDepthAtr)
                return null;

            if (ctx.PullbackBars_M5 > maxPullbackBars)
                return null;

            // Reaction candle KÖTELEZŐ
            if (!ctx.HasReactionCandle_M5)
                return null;

            // Reaction legyen trend irányú
            if (!ctx.LastClosedBarInTrendDirection)
                score -= 10;

            // EMA distance sanity
            double distFromEma = System.Math.Abs(ctx.M5.Last(1).Close - ctx.Ema21_M5);
            if (distFromEma > ctx.AtrM5 * 1.2)
                score -= 8;

            // =====================================================
            // CONTEXT BONUSES
            // =====================================================

            if (ctx.M1TriggerInTrendDirection)
                score += 10;

            if (ctx.MarketState?.IsTrend == true)
                score += 6;

            if (ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 2 &&
                ctx.PullbackDepthAtr_M5 < 0.6)
            {
                score += 8;
            }

            if (ctx.IsPullbackDecelerating_M5)
                score += 5;

            // =====================================================
            // VOL REGIME SOFT
            // =====================================================
            if (ctx.MarketState?.IsLowVol == true)
                score -= 12;

            // =====================================================
            // FLAG PRIORITY (csak ha erős flag)
            // =====================================================
            if (ctx.IsValidFlagStructure_M5 && ctx.M1TriggerInTrendDirection)
                score -= 12;

            // =====================================================
            // FINAL SCORE GATE
            // =====================================================
            if (score < MinScore)
                return null;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"IDX_PULLBACK_4.0 dir={dir} score={score} " +
                    $"pbATR={ctx.PullbackDepthAtr_M5:F2} pbBars={ctx.PullbackBars_M5} fatigue={trendFatigue}"
            };
        }
    }
}
