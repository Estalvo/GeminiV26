using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;

namespace GeminiV26.EntryTypes.Crypto
{
    public abstract class CryptoPullbackEntryBase : IEntryType
    {
        public virtual EntryType Type => EntryType.Crypto_Pullback;

        protected abstract string SymbolTag { get; }
        protected abstract int MaxBarsSinceImpulse { get; }
        protected abstract double MaxPullbackDepth { get; }

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY", "[ENTRY][CRYPTO_PB][BLOCK_CTX]");

            TradeDirection dir = ResolveDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_VALID_DIRECTION", "[DIR][CRYPTO_ENTRY][INVALID_NONE]");

            if (!ctx.Structure.HasImpulse || !ctx.Structure.HasPullback)
                return Reject(ctx, dir, "NO_PULLBACK_STRUCTURE", "[ENTRY][CRYPTO_PB][BLOCK_STRUCTURE]");

            int attempts = dir == TradeDirection.Long ? ctx.ContinuationAttemptCountLong : ctx.ContinuationAttemptCountShort;
            if (attempts > 0)
                return Reject(ctx, dir, "REFIRE_BLOCK", "[ENTRY][CRYPTO_PB][REFIRE_BLOCK]");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            if (barsSinceImpulse < 0 || barsSinceImpulse > MaxBarsSinceImpulse)
                return Reject(ctx, dir, "STALE_PULLBACK", "[ENTRY][CRYPTO_PB][BLOCK_STALE]");

            bool unstableTransition = ctx.IsTransition_M5 && (!ctx.IsVolatilityAcceptable_Crypto || !ctx.IsAtrExpanding_M5);
            if (unstableTransition)
                return Reject(ctx, dir, "VOL_TRANSITION_BLOCK", "[ENTRY][CRYPTO_PB][VOL_TRANSITION_BLOCK]");

            bool pullbackComplete =
                ctx.Structure.PullbackConfirmedSignal ||
                (ctx.IsPullbackDecelerating_M5 && ctx.HasReactionCandle_M5 && ctx.LastClosedBarInTrendDirection);
            if (!pullbackComplete)
                return Reject(ctx, dir, "PULLBACK_NOT_COMPLETE", "[ENTRY][CRYPTO_PB][BLOCK_INCOMPLETE]");

            ctx.Log?.Invoke("[ENTRY][CRYPTO_PB][COMPLETE_OK]");

            bool directionalBreak =
                (dir == TradeDirection.Long && (ctx.Structure.FlagBreakoutUp || ctx.FlagBreakoutUpConfirmed || ctx.RangeBreakDirection == TradeDirection.Long)) ||
                (dir == TradeDirection.Short && (ctx.Structure.FlagBreakoutDown || ctx.FlagBreakoutDownConfirmed || ctx.RangeBreakDirection == TradeDirection.Short)) ||
                ctx.Structure.ContinuationConfirmedSignal;

            if (!directionalBreak)
                return Reject(ctx, dir, "NO_DIRECTIONAL_CONTINUATION_BREAK", "[ENTRY][CRYPTO_PB][BLOCK_DIR_BREAK]");

            ctx.Log?.Invoke("[ENTRY][CRYPTO_PB][DIR_BREAK_OK]");

            bool stackedOverlap =
                ctx.Structure.PullbackConfirmedSignal &&
                ctx.Structure.ContinuationConfirmedSignal &&
                (ctx.RangeBreakDirection == dir);
            if (stackedOverlap)
                return Reject(ctx, dir, "OVERLAP_STACK_BLOCK", "[ENTRY][CRYPTO_PB][BLOCK_OVERLAP]");

            if (ctx.Structure.PullbackDepth <= 0 || ctx.Structure.PullbackDepth > MaxPullbackDepth)
                return Reject(ctx, dir, "PULLBACK_DEPTH_INVALID", "[ENTRY][CRYPTO_PB][BLOCK_DEPTH]");

            int score = 64;
            if (ctx.Structure.PullbackConfirmedSignal)
                score += 8;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.M1TriggerInTrendDirection)
                score += 6;
            score = Math.Max(0, Math.Min(100, score));

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = $"{SymbolTag}_PULLBACK_STRUCTURE_FIRST_OK"
            };
        }

        private TradeDirection ResolveDirection(EntryContext ctx)
        {
            TradeDirection inputFinal = ctx?.FinalDirection ?? TradeDirection.None;
            TradeDirection inputLogic = ctx?.LogicBiasDirection ?? TradeDirection.None;
            ctx?.Log?.Invoke($"[DIR][CRYPTO_ENTRY][INPUT] symbol={ctx?.Symbol} final={inputFinal} logic={inputLogic}");

            if (inputFinal == TradeDirection.None && inputLogic == TradeDirection.None)
                return TradeDirection.None;

            if (inputFinal != TradeDirection.None)
            {
                if (inputLogic != TradeDirection.None && inputLogic != inputFinal)
                    ctx?.Log?.Invoke($"[DIR][CRYPTO_ENTRY][MISMATCH_WARN] symbol={ctx?.Symbol} final={inputFinal} logic={inputLogic}");

                ctx?.Log?.Invoke($"[DIR][CRYPTO_ENTRY][FINAL_OK] symbol={ctx?.Symbol} source=FinalDirection dir={inputFinal}");
                return inputFinal;
            }

            ctx?.Log?.Invoke($"[DIR][CRYPTO_ENTRY][MISMATCH_WARN] symbol={ctx?.Symbol} final=None logic={inputLogic} mode=advisory_fallback");
            ctx?.Log?.Invoke($"[DIR][CRYPTO_ENTRY][FINAL_OK] symbol={ctx?.Symbol} source=LogicBiasDirectionFallback dir={inputLogic}");
            return inputLogic;
        }

        protected EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, string reason, string log)
        {
            ctx?.Log?.Invoke(log);
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = false,
                Score = 0,
                Reason = reason
            };
        }
    }
}
