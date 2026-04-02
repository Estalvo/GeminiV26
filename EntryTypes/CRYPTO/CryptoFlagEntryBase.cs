using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;

namespace GeminiV26.EntryTypes.Crypto
{
    public abstract class CryptoFlagEntryBase : IEntryType
    {
        public virtual EntryType Type => EntryType.Crypto_Flag;

        protected abstract string SymbolTag { get; }
        protected abstract int MaxBarsSinceImpulse { get; }
        protected abstract int MaxLateBreakoutBars { get; }
        protected abstract double MinImpulseStrength { get; }
        protected virtual int MaxImpulseMemoryBars => MaxBarsSinceImpulse + 2;
        protected virtual int MaxBreakoutPersistenceBars => MaxLateBreakoutBars + 1;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY");

            TradeDirection dir = ResolveDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_VALID_DIRECTION");

            if (!ctx.Structure.HasImpulse || !ctx.Structure.HasFlag)
                return Reject(ctx, dir, "INVALID_STRUCTURE");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            if (barsSinceImpulse < 0)
                return Reject(ctx, dir, "NO_RECENT_IMPULSE");

            if (barsSinceImpulse > MaxImpulseMemoryBars)
                return Reject(ctx, dir, "LATE_FLAG");

            bool breakoutConfirmed = dir == TradeDirection.Long
                ? (ctx.FlagBreakoutUpConfirmed || ctx.Structure.FlagBreakoutUp)
                : (ctx.FlagBreakoutDownConfirmed || ctx.Structure.FlagBreakoutDown);

            int breakoutBarsSince = dir == TradeDirection.Long ? ctx.BreakoutUpBarsSince : ctx.BreakoutDownBarsSince;
            bool continuationSignal = ctx.Structure.ContinuationConfirmedSignal || ctx.RangeBreakDirection == dir;
            bool hasTrendFollowThrough = ctx.LastClosedBarInTrendDirection || ctx.HasReactionCandle_M5 || ctx.M1TriggerInTrendDirection;

            bool persistenceOk =
                ((breakoutConfirmed && breakoutBarsSince <= MaxBreakoutPersistenceBars) ||
                 (continuationSignal && breakoutBarsSince <= MaxLateBreakoutBars)) &&
                hasTrendFollowThrough;

            if (!persistenceOk)
                return Reject(ctx, dir, "LATE_FLAG");

            bool structureValid =
                ctx.Structure.FlagCompression <= 0.62 ||
                ctx.Structure.ContinuationConfirmedSignal ||
                ctx.M1TriggerInTrendDirection ||
                ctx.HasReactionCandle_M5;
            if (!structureValid)
                return Reject(ctx, dir, "INVALID_STRUCTURE");

            bool fakeBreak =
                ctx.RangeFakeoutBars_M1 <= 2 ||
                (!ctx.Structure.ContinuationConfirmedSignal && !ctx.IsAtrExpanding_M5) ||
                (ctx.Structure.ImpulseStrength < MinImpulseStrength && !ctx.M1TriggerInTrendDirection);

            if (fakeBreak)
                return Reject(ctx, dir, "FAKE_BREAKOUT");

            int score = 62;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.Structure.FlagCompression <= 0.40)
                score += 8;
            if (ctx.Structure.ContinuationConfirmedSignal)
                score += 8;
            score = Math.Max(0, Math.Min(100, score));

            var evaluation = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = $"{SymbolTag}_FLAG_STRUCTURE_FIRST_OK"
            };
            ctx.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][RECOGNIZED] symbol={ctx.Symbol} dir={dir} reason={evaluation.Reason} barsSinceImpulse={barsSinceImpulse} breakoutBarsSince={breakoutBarsSince} impulseStrength={ctx.Structure.ImpulseStrength:0.00} compression={ctx.Structure.FlagCompression:0.00}");
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
            ctx?.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][BLOCK] symbol={ctx?.Symbol} dir={dir} reason={reason}");
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
