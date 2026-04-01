using Gemini.Memory;
using GeminiV26.Instruments;
using GeminiV26.Core.Logging;
using System;

namespace GeminiV26.Core.Entry.Qualification
{
    public static class ContinuationPolicy
    {
        private const double WeakContinuationThreshold = 0.45;
        private const double TransitionCollapseThreshold = 0.30;

        private enum StructureQualityZone
        {
            HardBlock,
            Weak,
            Valid
        }

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
                state.TransitionQuality < WeakContinuationThreshold &&
                !state.HasImpulse;

            if (weakContinuation)
            {
                Log(ctx, "[ENTRY][FILTER][WEAK_CONTINUATION]",
                    $"TQ={state.TransitionQuality:0.00}");

                if (instrumentClass == InstrumentClass.CRYPTO)
                    return EntryDecision.Block("WEAK_CONTINUATION");

                return EntryDecision.Penalize(0.20, "WEAK_CONTINUATION");
            }

            if (state.TransitionQuality < TransitionCollapseThreshold)
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

            if (isPullbackEntry || isFlagEntry || isBreakoutEntry)
            {
                var structureZone = ResolveStructureZone(
                    isPullbackEntry,
                    isFlagEntry,
                    isBreakoutEntry,
                    pullbackDepth,
                    flagToImpulseRatio,
                    flagBars,
                    barsSinceImpulse);

                string metrics = ResolveStructureMetrics(
                    isPullbackEntry,
                    isFlagEntry,
                    isBreakoutEntry,
                    pullbackDepth,
                    flagToImpulseRatio,
                    flagBars,
                    barsSinceImpulse);

                if (isBreakoutEntry)
                {
                    Log(ctx, "[ENTRY][WARN][BREAKOUT_TIMING_DUPLICATION]",
                        $"barsSinceImpulse={barsSinceImpulse} layer=qualification");
                }

                Log(ctx, "[ENTRY][STRUCTURE_ZONE]",
                    $"symbol={ctx.Symbol} type={entryTypeName} zone={structureZone} metrics={metrics}");

                if (structureZone == StructureQualityZone.HardBlock)
                {
                    string hardBlockReason = ResolveHardBlockReason(isPullbackEntry, isFlagEntry, isBreakoutEntry);
                    Log(ctx, "[ENTRY][BLOCK][STRUCTURE_HARD_BLOCK]",
                        $"symbol={ctx.Symbol} type={entryTypeName} reason={hardBlockReason}");
                    return EntryDecision.Block(hardBlockReason);
                }

                if (structureZone == StructureQualityZone.Weak)
                {
                    string weakReason = ResolveWeakReason(isPullbackEntry, isFlagEntry, isBreakoutEntry);

                    if (instrumentClass == InstrumentClass.CRYPTO)
                    {
                        Log(ctx, "[ENTRY][BLOCK][STRUCTURE_WEAK_CRYPTO]",
                            $"symbol={ctx.Symbol} type={entryTypeName} reason={weakReason}");
                        return EntryDecision.Block(weakReason);
                    }

                    Log(ctx, "[ENTRY][FILTER][WEAK_STRUCTURE]",
                        $"symbol={ctx.Symbol} type={entryTypeName} reason={weakReason}");
                    return EntryDecision.Penalize(0.20, weakReason);
                }
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

        private static StructureQualityZone ResolveStructureZone(
            bool isPullbackEntry,
            bool isFlagEntry,
            bool isBreakoutEntry,
            double pullbackDepth,
            double flagToImpulseRatio,
            int flagBars,
            int barsSinceImpulse)
        {
            if (isPullbackEntry)
                return ResolvePullbackZone(pullbackDepth);

            if (isFlagEntry)
                return ResolveFlagZone(flagToImpulseRatio, flagBars);

            if (isBreakoutEntry)
                return ResolveBreakoutZone(barsSinceImpulse);

            return StructureQualityZone.Valid;
        }

        private static StructureQualityZone ResolvePullbackZone(double pullbackDepth)
        {
            if (pullbackDepth < 0.20)
                return StructureQualityZone.HardBlock;

            if (pullbackDepth < 0.25)
                return StructureQualityZone.Weak;

            return StructureQualityZone.Valid;
        }

        private static StructureQualityZone ResolveFlagZone(double flagToImpulseRatio, int flagBars)
        {
            if (flagToImpulseRatio > 0 && flagToImpulseRatio < 0.15)
                return StructureQualityZone.HardBlock;

            if (flagBars < 2)
                return StructureQualityZone.Weak;

            return StructureQualityZone.Valid;
        }

        private static StructureQualityZone ResolveBreakoutZone(int barsSinceImpulse)
        {
            if (barsSinceImpulse > 8)
                return StructureQualityZone.HardBlock;

            if (barsSinceImpulse > 5)
                return StructureQualityZone.Weak;

            return StructureQualityZone.Valid;
        }

        private static string ResolveStructureMetrics(
            bool isPullbackEntry,
            bool isFlagEntry,
            bool isBreakoutEntry,
            double pullbackDepth,
            double flagToImpulseRatio,
            int flagBars,
            int barsSinceImpulse)
        {
            if (isPullbackEntry)
                return $"depth={pullbackDepth:0.00}";

            if (isFlagEntry)
                return $"ratio={flagToImpulseRatio:0.00} flagBars={flagBars}";

            if (isBreakoutEntry)
                return $"barsSinceImpulse={barsSinceImpulse}";

            return "n/a";
        }

        private static string ResolveHardBlockReason(bool isPullbackEntry, bool isFlagEntry, bool isBreakoutEntry)
        {
            if (isPullbackEntry)
                return "PULLBACK_TOO_SHALLOW";

            if (isFlagEntry)
                return "ULTRA_FLAT_FLAG";

            if (isBreakoutEntry)
                return "BREAKOUT_TOO_LATE";

            return "STRUCTURE_HARD_BLOCK";
        }

        private static string ResolveWeakReason(bool isPullbackEntry, bool isFlagEntry, bool isBreakoutEntry)
        {
            if (isPullbackEntry)
                return "PULLBACK_TOO_SHALLOW";

            if (isFlagEntry)
                return "FLAG_TOO_SHORT";

            if (isBreakoutEntry)
                return "BREAKOUT_TOO_LATE";

            return "WEAK_STRUCTURE";
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
            GlobalLogger.Log($"{tag} symbol={ctx.Symbol} {details}".TrimEnd(), ctx?.Bot);
        }
    }
}
