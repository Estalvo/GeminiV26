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
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Invalid(ctx, "NO_FX_PROFILE");

            return ctx.Session switch
            {
                FxSession.Asia => EvaluateAsia(ctx, fx),
                FxSession.London => EvaluateLondon(ctx, fx),
                FxSession.NewYork => EvaluateNewYork(ctx, fx),
                _ => Invalid(ctx, "NO_SESSION")
            };
        }

        // =================================================
        // ASIA – very strict, impulse required
        // =================================================
        private EntryEvaluation EvaluateAsia(EntryContext ctx, FxInstrumentProfile fx)
        {
            if (!fx.AllowAsianSession)
                return Invalid(ctx, "ASIA_DISABLED");

            if (!ResolveTrend(ctx, out var dir))
                return Invalid(ctx, "NO_TREND_ASIA");

            if (!HasFreshImpulse(ctx))
                return Invalid(ctx, "NO_IMPULSE_ASIA");

            if (!BasicPullbackChecks(ctx))
                return Invalid(ctx, "PB_BASE_FAIL_ASIA");

            var eval = BaseEval(ctx, dir, 55);
            ApplySessionAndHtf(ctx, ref eval);

            eval.Reason = "PB_ASIA_OK;";
            return eval;
        }

        // =================================================
        // LONDON – MAIN FX PULLBACK
        // =================================================
        private EntryEvaluation EvaluateLondon(EntryContext ctx, FxInstrumentProfile fx)
        {
            if (!ResolveTrend(ctx, out var dir))
                return Invalid(ctx, "NO_TREND_LONDON");

            if (!HasFreshImpulse(ctx))
                return Invalid(ctx, "NO_IMPULSE_LONDON");

            if (!BasicPullbackChecks(ctx))
                return Invalid(ctx, "PB_BASE_FAIL_LONDON");

            var eval = BaseEval(ctx, dir, 85);

            if (ctx.IsAtrExpanding_M5)
                eval.Score -= fx.PbLondonAtrExpandPenalty;

            if (ctx.IsValidFlagStructure_M5)
                eval.Score -= fx.PbLondonFlagPriorityPenalty;

            ApplySessionAndHtf(ctx, ref eval);

            eval.Reason = "PB_LONDON_OK;";
            return eval;
        }

        // =================================================
        // NEW YORK – CONFIRMATION ONLY
        // =================================================
        private EntryEvaluation EvaluateNewYork(EntryContext ctx, FxInstrumentProfile fx)
        {
            if (!ResolveTrend(ctx, out var dir))
                return Invalid(ctx, "NO_TREND_NY");

            if (!HasFreshImpulse(ctx))
                return Invalid(ctx, "NO_IMPULSE_NY");

            if (!BasicPullbackChecks(ctx))
                return Invalid(ctx, "PB_BASE_FAIL_NY");

            if (ctx.IsAtrExpanding_M5)
                return Invalid(ctx, "VOL_EXPANDING_NY");

            var eval = BaseEval(ctx, dir, 75);

            if (!ctx.M1TriggerInTrendDirection)
                eval.Score -= fx.PbNyNoM1Penalty;

            if (ctx.IsValidFlagStructure_M5)
                eval.Score -= fx.PbNyFlagPriorityPenalty;

            ApplySessionAndHtf(ctx, ref eval);

            eval.Reason = "PB_NY_OK;";
            return eval;
        }

        // =================================================
        // CORE LOGIC
        // =================================================
        private bool ResolveTrend(EntryContext ctx, out TradeDirection dir)
        {
            dir = TradeDirection.None;

            bool up =
                ctx.Ema21Slope_M15 > 0 &&
                ctx.Ema21Slope_M5 > 0;

            bool down =
                ctx.Ema21Slope_M15 < 0 &&
                ctx.Ema21Slope_M5 < 0;

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
            return ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 5;
        }

        private bool BasicPullbackChecks(EntryContext ctx)
        {
            if (!ctx.PullbackTouchedEma21_M5)
                return false;

            if (!ctx.IsPullbackDecelerating_M5)
                return false;

            if (!ctx.HasReactionCandle_M5 && !ctx.HasRejectionWick_M5)
                return false;

            if (!ctx.LastClosedBarInTrendDirection)
                return false;

            if (ctx.PullbackDepthAtr_M5 > 1.0)
                return false;

            return true;
        }

        private void ApplySessionAndHtf(EntryContext ctx, ref EntryEvaluation eval)
        {
            eval.Score = Helpers.FxSessionScoreHelper
                .ApplySessionDelta(ctx.Symbol, ctx.Session, eval.Score);

            if (ctx.FxHtfAllowedDirection == TradeDirection.None)
                eval.Score -= 10;
        }

        private EntryEvaluation BaseEval(
            EntryContext ctx,
            TradeDirection dir,
            int score)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true
            };
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason + ";"
            };
        }
    }
}
