using System;
using GeminiV26.Core.Logging;

namespace GeminiV26.Core.Entry.Qualification
{
    public class EntryState
    {
        public bool HasDirectionalBias { get; set; }
        public bool HasImpulse { get; set; }
        public double TransitionQuality { get; set; }
        public bool HasTransition { get; set; }
        public bool HasMomentum { get; set; }
        public bool HasStructure { get; set; }
        public bool HasTrend { get; set; }
        public bool IsDeadMarket { get; set; }
    }

    public static class EntryStateEvaluator
    {
        private const double TransitionThreshold = 0.42;
        private const double MomentumThreshold = 0.47;
        private const double StructureThreshold = 0.52;

        public static EntryState Evaluate(EntryContext ctx)
        {
            var state = new EntryState();

            if (ctx == null)
                return state;

            // -------------------------
            // DIRECTIONAL BIAS
            // -------------------------
            state.HasDirectionalBias =
                ctx.LogicBiasDirection != TradeDirection.None;

            // -------------------------
            // IMPULSE
            // -------------------------
            state.HasImpulse =
                ctx.HasImpulse_M5 ||
                ctx.HasImpulseLong_M5 ||
                ctx.HasImpulseShort_M5;

            // -------------------------
            // TRANSITION
            // -------------------------
            double rawTQ = ctx.Transition?.QualityScore ?? 0.0;
            double tq = ctx.Transition?.QualityScore01 ?? 0.0;
            state.TransitionQuality = tq;

            state.HasTransition = tq >= TransitionThreshold;

            // -------------------------
            // MOMENTUM (STRICT)
            // -------------------------
            state.HasMomentum =
                state.HasImpulse ||
                tq >= MomentumThreshold;

            // -------------------------
            // STRUCTURE (STRICTER THAN BEFORE)
            // -------------------------
            state.HasStructure =
                state.HasImpulse ||
                tq >= StructureThreshold;

            // -------------------------
            // TREND (STRICT)
            // -------------------------
            state.HasTrend =
                state.HasDirectionalBias &&
                state.HasStructure;

            Log(ctx,
                $"[ENTRY][STATE][TREND_EVAL] rawTQ={rawTQ:0.00} tq={tq:0.00} trend={state.HasTrend.ToString().ToLowerInvariant()}");
            Log(ctx,
                $"[ENTRY][STATE][MOMENTUM_EVAL] rawTQ={rawTQ:0.00} tq={tq:0.00} momentum={state.HasMomentum.ToString().ToLowerInvariant()}");
            Log(ctx,
                $"[QUAL][TQ] tq={tq:0.00} hasTransition={state.HasTransition.ToString().ToLowerInvariant()} hasMomentum={state.HasMomentum.ToString().ToLowerInvariant()} hasStructure={state.HasStructure.ToString().ToLowerInvariant()}");

            // -------------------------
            // DEAD MARKET
            // -------------------------
            state.IsDeadMarket =
                !state.HasTrend &&
                !state.HasMomentum;

            return state;
        }

        private static void Log(EntryContext ctx, string message)
        {
            if (ctx?.Log != null)
            {
                ctx.Log(message);
                return;
            }

            if (ctx?.Bot != null)
                GlobalLogger.Log(ctx.Bot, message);
        }
    }
}
