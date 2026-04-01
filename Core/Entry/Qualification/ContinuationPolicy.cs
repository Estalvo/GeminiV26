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

            string entryTypeName = entryType.ToString();
            bool isFlagEntry = entryTypeName.IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isPullbackEntry = entryTypeName.IndexOf("Pullback", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isBreakoutEntry = entryTypeName.IndexOf("Breakout", StringComparison.OrdinalIgnoreCase) >= 0;

            int flagBars = Math.Max(ctx.FlagBarsLong_M5, ctx.FlagBarsShort_M5);
            double pullbackDepth = Math.Max(ctx.PullbackDepthRLong_M5, ctx.PullbackDepthRShort_M5);
            double flagToImpulseRatio = ResolveFlagToImpulseRatio(ctx);
            int barsSinceImpulse = ctx.Memory?.BarsSinceImpulse ?? ctx.BarsSinceImpulse_M5;

            if (isFlagEntry && flagToImpulseRatio > 0 && flagToImpulseRatio < 0.15)
            {
                Log(ctx, "[ENTRY][BLOCK][ULTRA_FLAT_FLAG]",
                    $"symbol={ctx.Symbol} type={entryTypeName} ratio={flagToImpulseRatio:0.00}");
                return EntryDecision.Block("ULTRA_FLAT_FLAG");
            }

            if (isPullbackEntry && pullbackDepth < 0.20)
            {
                Log(ctx, "[ENTRY][BLOCK][PULLBACK_TOO_SHALLOW]",
                    $"symbol={ctx.Symbol} type={entryTypeName} depth={pullbackDepth:0.00}");
                return EntryDecision.Block("PULLBACK_TOO_SHALLOW");
            }

            if (isBreakoutEntry && barsSinceImpulse > 8)
            {
                Log(ctx, "[ENTRY][BLOCK][BREAKOUT_TOO_LATE]",
                    $"symbol={ctx.Symbol} type={entryTypeName} barsSinceImpulse={barsSinceImpulse}");
                return EntryDecision.Block("BREAKOUT_TOO_LATE");
            }

            bool weakStructure = false;
            string weakReason = string.Empty;

            if (isFlagEntry && flagBars < 2)
            {
                weakStructure = true;
                weakReason = "FLAG_TOO_SHORT";
            }

            if (isPullbackEntry && pullbackDepth < 0.25)
            {
                weakStructure = true;
                weakReason = "PULLBACK_TOO_SHALLOW";
            }

            if (isBreakoutEntry && barsSinceImpulse > 5)
            {
                weakStructure = true;
                weakReason = "BREAKOUT_TOO_LATE";
            }

            if (weakStructure)
            {
                Log(ctx, "[ENTRY][FILTER][WEAK_STRUCTURE]",
                    $"symbol={ctx.Symbol} type={entryTypeName} reason={weakReason}");

                if (instrumentClass == InstrumentClass.CRYPTO)
                    return EntryDecision.Block(weakReason);

                return EntryDecision.Penalize(0.20, weakReason);
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

        private static double ResolveFlagToImpulseRatio(EntryContext ctx)
        {
            double ratio = 0.0;

            if (ctx.TransitionLong != null && ctx.TransitionLong.HasFlag && ctx.TransitionLong.CompressionScore > 0)
                ratio = Math.Max(ratio, ctx.TransitionLong.CompressionScore);

            if (ctx.TransitionShort != null && ctx.TransitionShort.HasFlag && ctx.TransitionShort.CompressionScore > 0)
                ratio = Math.Max(ratio, ctx.TransitionShort.CompressionScore);

            if (ratio > 0)
                return ratio;

            if (ctx.AtrM5 > 0 && ctx.FlagAtr_M5 > 0)
                return ctx.FlagAtr_M5 / ctx.AtrM5;

            return 0.0;
        }

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
