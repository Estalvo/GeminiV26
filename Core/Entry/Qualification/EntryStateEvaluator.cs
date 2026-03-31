using System;

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
            state.TransitionQuality = ctx.Transition?.QualityScore ?? 0.0;

            state.HasTransition = state.TransitionQuality >= 0.55;

            // -------------------------
            // MOMENTUM (STRICT)
            // -------------------------
            state.HasMomentum =
                state.HasImpulse ||
                state.TransitionQuality >= 0.60;

            // -------------------------
            // STRUCTURE (STRICTER THAN BEFORE)
            // -------------------------
            state.HasStructure =
                state.HasImpulse ||
                state.TransitionQuality >= 0.65;

            // -------------------------
            // TREND (STRICT)
            // -------------------------
            state.HasTrend =
                state.HasDirectionalBias &&
                state.HasStructure;

            // -------------------------
            // DEAD MARKET
            // -------------------------
            state.IsDeadMarket =
                !state.HasTrend &&
                !state.HasMomentum;

            return state;
        }
    }
}
