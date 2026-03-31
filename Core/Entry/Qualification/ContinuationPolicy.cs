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

            if (IsFlagType(entryType))
            {
                int flagBars = Math.Max(ctx.FlagBarsLong_M5, ctx.FlagBarsShort_M5);

                if (flagBars < 2)
                {
                    Log(ctx, "[ENTRY][BLOCK][INVALID_FLAG]", $"flagBars={flagBars}");
                    return EntryDecision.Block("INVALID_FLAG");
                }
            }

            double transitionQuality = ctx.Transition?.QualityScore ?? 0.0;
            bool hasTransition = transitionQuality >= 0.55;
            bool hasImpulse = ctx.HasImpulse_M5 || ctx.HasImpulseLong_M5 || ctx.HasImpulseShort_M5;
            bool hasDirectionalBias = ctx.LogicBiasDirection != TradeDirection.None;
            bool hasStructure = hasImpulse || transitionQuality >= 0.55;
            bool hasTrend = hasDirectionalBias && hasStructure;
            bool hasMomentum = hasImpulse || transitionQuality >= 0.60;

            Log(ctx,
                "[ENTRY][STATE][TREND_EVAL]",
                $"bias={hasDirectionalBias.ToString().ToLowerInvariant()} structure={hasStructure.ToString().ToLowerInvariant()} result={hasTrend.ToString().ToLowerInvariant()}");
            Log(ctx,
                "[ENTRY][STATE][MOMENTUM_EVAL]",
                $"impulse={hasImpulse.ToString().ToLowerInvariant()} TQ={transitionQuality:0.00} result={hasMomentum.ToString().ToLowerInvariant()}");

            bool isDeadMarket = !hasTrend && !hasMomentum;
            if (isDeadMarket)
            {
                Log(ctx, "[ENTRY][BLOCK][DEAD_MARKET_STRICT]", string.Empty);
                return EntryDecision.Block("DEAD_MARKET_STRICT");
            }

            if (instrumentClass == InstrumentClass.CRYPTO && !hasImpulse)
            {
                Log(ctx, "[ENTRY][BLOCK][NO_IMPULSE_CRYPTO]", string.Empty);
                return EntryDecision.Block("NO_IMPULSE_CRYPTO");
            }

            bool isWeakContinuation = transitionQuality < 0.55 && !ctx.HasImpulse_M5;
            if (isWeakContinuation)
            {
                Log(ctx,
                    "[ENTRY][FILTER][WEAK_CONTINUATION]",
                    $"TQ={transitionQuality:0.00} impulse={ctx.HasImpulse_M5.ToString().ToLowerInvariant()}");

                if (instrumentClass == InstrumentClass.CRYPTO)
                    return EntryDecision.Block("WEAK_CONTINUATION_CRYPTO");

                return EntryDecision.Penalize(0.20, "WEAK_CONTINUATION");
            }

            if (transitionQuality < 0.30)
            {
                Log(ctx, "[ENTRY][BLOCK][TRANSITION_COLLAPSE]", $"TQ={transitionQuality:0.00}");
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

            if (!hasTransition)
            {
                Log(ctx, "[ENTRY][PENALTY][WEAK_STRUCTURE]", $"score={transitionQuality:0.##}");
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
