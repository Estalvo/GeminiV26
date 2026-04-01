using Gemini.Memory;
using GeminiV26.Instruments;
using System;

namespace GeminiV26.Core.Entry.Qualification
{
    public static class ContinuationPolicy
    {
        public static EntryDecision Evaluate(EntryContext ctx, EntryType entryType)
        {
            if (ctx == null)
                return EntryDecision.Pass();

            InstrumentClass instrumentClass = ResolveInstrumentClass(ctx);
            var state = ctx.QualificationState ?? EntryStateEvaluator.Evaluate(ctx);
            ctx.QualificationState = state;

            Log(ctx, "[ENTRY][STATE][SUMMARY]",
                $"trend={state.HasTrend.ToString().ToLowerInvariant()} momentum={state.HasMomentum.ToString().ToLowerInvariant()} TQ={state.TransitionQuality:0.00}");

            if (IsFlagType(entryType))
            {
                int flagBars = Math.Max(ctx.FlagBarsLong_M5, ctx.FlagBarsShort_M5);

                if (flagBars < 2)
                {
                    Log(ctx, "[ENTRY][BLOCK][INVALID_FLAG]", $"flagBars={flagBars}");
                    return EntryDecision.Block("INVALID_FLAG");
                }
            }

            if (state.IsDeadMarket)
            {
                Log(ctx, "[ENTRY][BLOCK][DEAD_MARKET_STRICT]", string.Empty);
                return EntryDecision.Block("DEAD_MARKET");
            }

            if (instrumentClass == InstrumentClass.INDEX)
            {
                if (!state.HasTrend && !state.HasMomentum)
                {
                    Log(ctx, "[ENTRY][INDEX][BLOCK]", "no_trend_no_momentum");
                    return EntryDecision.Block("INDEX_NO_STATE");
                }

                if (state.HasTrend && !state.HasMomentum)
                {
                    Log(ctx, "[ENTRY][INDEX][PENALTY]", string.Empty);
                    return EntryDecision.Penalize(0.20, "INDEX_NO_MOMENTUM");
                }
            }

            if (instrumentClass == InstrumentClass.CRYPTO && !state.HasImpulse)
            {
                Log(ctx, "[ENTRY][BLOCK][NO_IMPULSE_CRYPTO]", string.Empty);
                return EntryDecision.Block("NO_IMPULSE_CRYPTO");
            }

            bool weakContinuation =
                state.TransitionQuality < 0.55 &&
                !state.HasImpulse;

            if (weakContinuation)
            {
                Log(ctx, "[ENTRY][FILTER][WEAK_CONTINUATION]",
                    $"TQ={state.TransitionQuality:0.00}");

                if (instrumentClass == InstrumentClass.CRYPTO)
                    return EntryDecision.Block("WEAK_CONTINUATION");

                return EntryDecision.Penalize(0.20, "WEAK_CONTINUATION");
            }

            if (state.TransitionQuality < 0.30)
            {
                Log(ctx, "[ENTRY][BLOCK][TRANSITION_COLLAPSE]", $"TQ={state.TransitionQuality:0.00}");
                return EntryDecision.Block("TRANSITION_COLLAPSE");
            }

            SymbolMemoryState memory = ctx.Memory;
            if (memory != null)
            {
                if (memory.BarsSinceImpulse < 2)
                {
                    Log(ctx, "[ENTRY][BLOCK][TOO_EARLY]", string.Empty);
                    return EntryDecision.Block("TOO_EARLY");
                }

                if (memory.MoveAgeBars > 20)
                {
                    Log(ctx, "[ENTRY][BLOCK][TOO_LATE]", string.Empty);
                    return EntryDecision.Block("TOO_LATE");
                }
            }

            if (!state.HasTransition)
            {
                Log(ctx, "[ENTRY][PENALTY][WEAK_STRUCTURE]", $"score={state.TransitionQuality:0.##}");
                return EntryDecision.Penalize(0.15, "WEAK_STRUCTURE");
            }

            return EntryDecision.Pass();
        }

        private static bool IsFlagType(EntryType entryType)
            => entryType.ToString().IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0;

        private static InstrumentClass ResolveInstrumentClass(EntryContext ctx)
        {
            string symbol = ctx.Symbol ?? string.Empty;
            if (symbol.IndexOf("BTC", StringComparison.OrdinalIgnoreCase) >= 0 ||
                symbol.IndexOf("ETH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return InstrumentClass.CRYPTO;
            }

            if (symbol.IndexOf("XAU", StringComparison.OrdinalIgnoreCase) >= 0)
                return InstrumentClass.METAL;

            if (symbol.IndexOf("NAS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                symbol.IndexOf("US30", StringComparison.OrdinalIgnoreCase) >= 0 ||
                symbol.IndexOf("GER40", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return InstrumentClass.INDEX;
            }

            return InstrumentClass.FX;
        }

        private static void Log(EntryContext ctx, string tag, string details)
        {
            if (ctx.Log != null)
            {
                ctx.Log($"{tag} symbol={ctx.Symbol} {details}".TrimEnd());
                return;
            }

            Console.WriteLine($"{tag} symbol={ctx.Symbol} {details}".TrimEnd());
        }
    }
}
