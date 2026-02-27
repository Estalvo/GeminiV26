using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Pullback;

        private const int MIN_SCORE = 35;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 60; // baseline

            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", score);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Block(ctx, "NO_FX_PROFILE", score);

            // =========================
            // TREND RESOLUTION (SOFT)
            // =========================
            bool trendUp = ctx.Ema21Slope_M15 > 0 && ctx.Ema21Slope_M5 > 0;
            bool trendDown = ctx.Ema21Slope_M15 < 0 && ctx.Ema21Slope_M5 < 0;

            TradeDirection dir = TradeDirection.None;

            if (trendUp) dir = TradeDirection.Long;
            else if (trendDown) dir = TradeDirection.Short;
            else score -= 12; // nincs tiszta trend

            if (dir == TradeDirection.None)
            {
                // FlagEntry parity: ha nincs tiszta trend irány, NE erőltessünk pullback belépőt.
                Console.WriteLine($"[FX_PB] BLOCK NO_TREND_DIR | {ctx.Symbol} | Session={ctx.Session} | score={score:0.0}");
                return Block(ctx, "NO_TREND_DIR", (int)Math.Round(score));
            }

            // =========================
            // IMPULSE QUALITY
            // =========================
            if (!ctx.HasImpulse_M5)
                score -= 8;
            else
            {
                if (ctx.BarsSinceImpulse_M5 <= 2)
                    score += 6;
                else if (ctx.BarsSinceImpulse_M5 <= 5)
                    score += 2;
                else
                    score -= 6;
            }

            // =========================
            // PULLBACK QUALITY
            // =========================
            if (!ctx.PullbackTouchedEma21_M5)
                score -= 6;

            if (!ctx.IsPullbackDecelerating_M5)
                score -= 6;

            if (!ctx.HasReactionCandle_M5)
                score -= 4;

            if (!ctx.LastClosedBarInTrendDirection)
                score -= 6;

            if (ctx.PullbackDepthAtr_M5 > 1.6)
                return Block(ctx, "PB_TOO_DEEP_EXTREME", score);

            if (ctx.PullbackDepthAtr_M5 > 1.0)
                score -= 6;

            // =========================
            // ENERGY MODEL (CORE)
            // =========================
            int fuel = 0;

            if (ctx.Adx_M5 >= 25) fuel += 4;
            else fuel -= 6;

            if (ctx.AdxSlope_M5 > 0) fuel += 4;
            else fuel -= 4;

            if (ctx.IsAtrExpanding_M5) fuel += 3;
            else fuel -= 3;

            if (ctx.BarsSinceImpulse_M5 <= 2) fuel += 4;

            score += fuel;

            // Hard exhaustion
            if (ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0 &&
                !ctx.IsAtrExpanding_M5)
            {
                return Block(ctx, "TREND_EXHAUSTION", score);
            // FlagEntry parity: low-energy hard blocks (ne nyissunk "lecsorgó/chop" környezetben)
            // LOW_ENERGY_NO_TREND: ADX már a saját átlag alatt, és meredeken esik → gyakran range/chop.
            var dynamicMinAdx = 14.0;
            if (ctx.Session == FxSession.Asia) dynamicMinAdx = 18.0;
            if (!ctx.HasImpulseM5) dynamicMinAdx += 2.0;

            if (ctx.Adx_M5 < dynamicMinAdx * 0.85 && ctx.AdxSlope_M5 < -0.02)
                return Block(ctx, "LOW_ENERGY_NO_TREND", score);

            // VERY_LOW_ADX: hard floor
            if (ctx.Adx_M5 < Math.Max(7.0, dynamicMinAdx * 0.65))
                return Block(ctx, "VERY_LOW_ADX", score);

            // ADX_EXHAUSTION_BLOCK: ha extrém magas volt az energia és hirtelen kifullad (FlagEntry küszöb)
            if (ctx.Adx_M5 > 45.0 && ctx.AdxSlope_M5 < -0.06)
                return Block(ctx, "ADX_EXHAUSTION_BLOCK", score);

            }

            // =========================
            // SESSION MODULATION
            // =========================
            if (ctx.Session == FxSession.Asia)
            {
                score -= 6;
                if (!ctx.HasImpulse_M5)
                    score -= 6;
            }

            if (ctx.Session == FxSession.NewYork)
            {
                if (!ctx.M1TriggerInTrendDirection)
                    score -= 6;
            }

            // =========================
            // FLAG PRIORITY (ne ütközzön)
            // =========================
            if (ctx.IsValidFlagStructure_M5)
                score -= fx.PbLondonFlagPriorityPenalty;

            // =========================
            // HTF SOFT
            // =========================
            if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfAllowedDirection != dir)
            {
                double conf = ctx.FxHtfConfidence01;
                int penalty = (int)(conf * 10);
                score -= penalty;

                if (conf >= 0.75 && fuel < 3)
                    return Block(ctx, "HTF_DOMINANT_BLOCK", score);
            }

            // =========================
            // FINAL SCORE CHECK
            // =========================
            if (score < MIN_SCORE)
                return Block(ctx, $"LOW_SCORE_{score}", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"FX_PULLBACK_V2 dir={dir} score={score}"
            };
        }

        private EntryEvaluation Block(
            EntryContext ctx,
            string reason,
            int score)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = TradeDirection.None,
                Score = score,
                IsValid = false,
                Reason = reason
            };
        }
    }
}