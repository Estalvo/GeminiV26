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
        protected virtual double MinFuelAtr => 0.30;
        protected virtual double MinFuelImpulseStrength => 0.34;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY");

            TradeDirection dir = ResolveDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_VALID_DIRECTION");

            if (!ctx.Structure.HasImpulse || !ctx.Structure.HasPullback)
                return Reject(ctx, dir, "NO_PULLBACK_STRUCTURE");

            int attempts = dir == TradeDirection.Long ? ctx.ContinuationAttemptCountLong : ctx.ContinuationAttemptCountShort;
            if (attempts > 0)
                return Reject(ctx, dir, "REFIRE_BLOCK");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            if (barsSinceImpulse < 0 || barsSinceImpulse > MaxBarsSinceImpulse)
                return Reject(ctx, dir, "STALE_PULLBACK");

            TradeDirection impulseDirection = ctx.ImpulseDirection == TradeDirection.None ? dir : ctx.ImpulseDirection;
            bool immediateCounter = barsSinceImpulse <= 0 && impulseDirection != dir;
            if (immediateCounter)
                return Reject(ctx, dir, "IMPULSE_LOCK_IMMEDIATE_COUNTER");

            double fuelAtr = Math.Max(0.0, ctx.TotalMoveSinceBreakAtr);
            bool hasFuel =
                fuelAtr >= MinFuelAtr ||
                ctx.Structure.ImpulseStrength >= MinFuelImpulseStrength ||
                (ctx.IsAtrExpanding_M5 && ctx.Structure.ContinuationConfirmedSignal);
            if (!hasFuel)
                return Reject(ctx, dir, "CRYPTO_PULLBACK_NO_FUEL");

            bool unstableTransition = ctx.IsTransition_M5 && !ctx.IsVolatilityAcceptable_Crypto && !ctx.IsAtrExpanding_M5;
            if (unstableTransition)
                return Reject(ctx, dir, "VOL_TRANSITION_BLOCK");

            int maturitySignals = 0;
            if (ctx.IsPullbackDecelerating_M5) maturitySignals++;
            if (ctx.HasReactionCandle_M5) maturitySignals++;
            if (ctx.LastClosedBarInTrendDirection) maturitySignals++;
            bool pullbackComplete =
                ctx.Structure.PullbackConfirmedSignal ||
                maturitySignals >= 2 ||
                (barsSinceImpulse >= 3 && maturitySignals >= 1);
            if (!pullbackComplete)
                return Reject(ctx, dir, "PULLBACK_NOT_MATURE");

            bool directionalBreak =
                (dir == TradeDirection.Long && (ctx.Structure.FlagBreakoutUp || ctx.FlagBreakoutUpConfirmed || ctx.RangeBreakDirection == TradeDirection.Long)) ||
                (dir == TradeDirection.Short && (ctx.Structure.FlagBreakoutDown || ctx.FlagBreakoutDownConfirmed || ctx.RangeBreakDirection == TradeDirection.Short)) ||
                ctx.Structure.ContinuationConfirmedSignal;

            if (!directionalBreak)
                return Reject(ctx, dir, "NO_DIRECTIONAL_CONTINUATION_BREAK");

            bool stackedOverlap =
                ctx.Structure.PullbackConfirmedSignal &&
                ctx.Structure.ContinuationConfirmedSignal &&
                (ctx.RangeBreakDirection == dir) &&
                !ctx.M1TriggerInTrendDirection &&
                !ctx.LastClosedBarInTrendDirection;
            if (stackedOverlap)
                return Reject(ctx, dir, "OVERLAP_STACK_BLOCK");

            if (ctx.Structure.PullbackDepth <= 0 || ctx.Structure.PullbackDepth > MaxPullbackDepth)
                return Reject(ctx, dir, "PULLBACK_DEPTH_INVALID");

            int score = 64;
            if (ctx.Structure.PullbackConfirmedSignal)
                score += 8;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.M1TriggerInTrendDirection)
                score += 6;
            score = Math.Max(0, Math.Min(100, score));

            var evaluation = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = $"{SymbolTag}_PULLBACK_STRUCTURE_FIRST_OK"
            };
            ctx.Log?.Invoke($"[ENTRY][CRYPTO_PB][RECOGNIZED] symbol={ctx.Symbol} dir={dir} reason={evaluation.Reason} barsSinceImpulse={barsSinceImpulse} fuelAtr={fuelAtr:0.00} impulseStrength={ctx.Structure.ImpulseStrength:0.00} maturitySignals={maturitySignals} pullbackDepth={ctx.Structure.PullbackDepth:0.00}");
            return evaluation;
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

        protected EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, string reason)
        {
            ctx?.Log?.Invoke($"[ENTRY][CRYPTO_PB][BLOCK] symbol={ctx?.Symbol} dir={dir} reason={reason}");
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
