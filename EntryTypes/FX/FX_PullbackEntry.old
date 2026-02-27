using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Pullback;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 0;

            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY", score);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Invalid(ctx, "NO_FX_PROFILE", score);

            return ctx.Session switch
            {
                FxSession.Asia => EvaluateAsia(ctx, fx, ref score),
                FxSession.London => EvaluateLondon(ctx, fx, ref score),
                FxSession.NewYork => EvaluateNewYork(ctx, fx, ref score),
                _ => Invalid(ctx, "NO_SESSION", score)
            };
        }

        // =================================================
        // ASIA – VERY STRICT
        // =================================================
        private EntryEvaluation EvaluateAsia(
            EntryContext ctx,
            FxInstrumentProfile fx,
            ref int score)
        {
            if (!fx.AllowAsianSession)
                return Invalid(ctx, "ASIA_DISABLED", score);

            if (!ResolveTrend(ctx, out var dir))
                return Invalid(ctx, "NO_TREND_ASIA", score);

            if (!HasFreshImpulse(ctx))
                return Invalid(ctx, "NO_IMPULSE_ASIA", score);

            if (IsLateEntryAfterImpulse(ctx, 3))
                return Invalid(ctx, "ASIA_LATE_PB", score);

            if (!BasicPullbackChecks(ctx))
                return Invalid(ctx, "PB_BASE_FAIL_ASIA", score);

            score = 55;

            ApplyHtfSoft(ctx, ref score);

            if (score < 30)
                return Invalid(ctx, $"LOW_SCORE({score})", score);

            return Valid(ctx, dir, score, "FX_PB_ASIA");
        }

        // =================================================
        // LONDON – MAIN FX PULLBACK
        // =================================================
        private EntryEvaluation EvaluateLondon(
            EntryContext ctx,
            FxInstrumentProfile fx,
            ref int score)
        {
            if (!ResolveTrend(ctx, out var dir))
                return Invalid(ctx, "NO_TREND_LONDON", score);

            if (!HasFreshImpulse(ctx))
                return Invalid(ctx, "NO_IMPULSE_LONDON", score);

            if (IsLateEntryAfterImpulse(ctx, 6))
                return Invalid(ctx, "LONDON_LATE_PB", score);

            if (!BasicPullbackChecks(ctx))
                return Invalid(ctx, "PB_BASE_FAIL_LONDON", score);

            score = 85;

            if (ctx.IsAtrExpanding_M5)
                score -= fx.PbLondonAtrExpandPenalty;

            if (ctx.IsValidFlagStructure_M5)
                score -= fx.PbLondonFlagPriorityPenalty;

            ApplyHtfSoft(ctx, ref score);

            // FX PULLBACK SOFT FLOOR
            if (score < 0 && score >= -15)
                score = 25;

            if (score < 30)
                return Invalid(ctx, $"LOW_SCORE({score})", score);

            return Valid(ctx, dir, score, "FX_PB_LONDON");
        }

        // =================================================
        // NEW YORK – CONFIRMATION ONLY
        // =================================================
        private EntryEvaluation EvaluateNewYork(
            EntryContext ctx,
            FxInstrumentProfile fx,
            ref int score)
        {
            if (!ResolveTrend(ctx, out var dir))
                return Invalid(ctx, "NO_TREND_NY", score);

            if (!HasFreshImpulse(ctx))
                return Invalid(ctx, "NO_IMPULSE_NY", score);

            if (IsLateEntryAfterImpulse(ctx, 5))
                return Invalid(ctx, "NY_LATE_PB", score);

            if (!BasicPullbackChecks(ctx))
                return Invalid(ctx, "PB_BASE_FAIL_NY", score);

            if (ctx.IsAtrExpanding_M5)
                return Invalid(ctx, "VOL_EXPANDING_NY", score);

            score = 75;

            if (!ctx.M1TriggerInTrendDirection)
                score -= fx.PbNyNoM1Penalty;

            if (ctx.IsValidFlagStructure_M5)
                score -= fx.PbNyFlagPriorityPenalty;

            ApplyHtfSoft(ctx, ref score);

            if (score < 0 && score >= -15)
                score = 23;

            if (score < 30)
                return Invalid(ctx, $"LOW_SCORE({score})", score);

            return Valid(ctx, dir, score, "FX_PB_NY");
        }

        // =================================================
        // CORE HELPERS
        // =================================================
        private bool ResolveTrend(EntryContext ctx, out TradeDirection dir)
        {
            dir = TradeDirection.None;

            bool up = ctx.Ema21Slope_M15 > 0 && ctx.Ema21Slope_M5 > 0;
            bool down = ctx.Ema21Slope_M15 < 0 && ctx.Ema21Slope_M5 < 0;

            if (!up && !down)
                return false;

            dir = up ? TradeDirection.Long : TradeDirection.Short;

            if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfAllowedDirection != dir)
                return false;

            return true;
        }

        private bool HasFreshImpulse(EntryContext ctx)
        {
            return ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 8;
        }

        private bool BasicPullbackChecks(EntryContext ctx)
        {
            if (!ctx.PullbackTouchedEma21_M5)
                return false;

            if (!ctx.IsPullbackDecelerating_M5)
                return false;

            if (!ctx.LastClosedBarInTrendDirection)
                return false;

            if (ctx.PullbackDepthAtr_M5 > 1.0)
                return false;

            return true;
        }

        private static void ApplyHtfSoft(EntryContext ctx, ref int score)
        {
            if (ctx.FxHtfConfidence01 > 0.8 &&
                ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfAllowedDirection != ctx.TrendDirection)
            {
                score -= 10;
            }
        }

        private static bool IsLateEntryAfterImpulse(EntryContext ctx, int maxBars)
        {
            return ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 > maxBars;
        }

        // =================================================
        // RESULT BUILDERS
        // =================================================
        private static EntryEvaluation Valid(
            EntryContext ctx,
            TradeDirection dir,
            int score,
            string tag)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"{tag} dir={dir} score={score}"
            };
        }

        private static EntryEvaluation Invalid(
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
                Reason = $"{reason} rawScore={score}"
            };
        }
    }
}
