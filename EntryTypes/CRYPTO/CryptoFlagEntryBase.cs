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

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY", "[ENTRY][CRYPTO_FLAG][BLOCK_CTX]");

            TradeDirection dir = ResolveDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_VALID_DIRECTION", "[DIR][CRYPTO_ENTRY][INVALID_NONE]");

            if (!ctx.Structure.HasImpulse || !ctx.Structure.HasFlag)
                return Reject(ctx, dir, "NO_FLAG_STRUCTURE", "[ENTRY][CRYPTO_FLAG][BLOCK_STRUCTURE]");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            if (barsSinceImpulse < 0 || barsSinceImpulse > MaxBarsSinceImpulse)
                return Reject(ctx, dir, "LATE_FLAG", "[ENTRY][CRYPTO_FLAG][LATE_BLOCK]");

            bool breakoutConfirmed = dir == TradeDirection.Long
                ? (ctx.FlagBreakoutUpConfirmed || ctx.Structure.FlagBreakoutUp)
                : (ctx.FlagBreakoutDownConfirmed || ctx.Structure.FlagBreakoutDown);

            int breakoutBarsSince = dir == TradeDirection.Long ? ctx.BreakoutUpBarsSince : ctx.BreakoutDownBarsSince;

            bool persistenceOk =
                breakoutConfirmed &&
                breakoutBarsSince <= MaxLateBreakoutBars &&
                ctx.LastClosedBarInTrendDirection &&
                (ctx.HasReactionCandle_M5 || ctx.M1TriggerInTrendDirection);

            if (!persistenceOk)
                return Reject(ctx, dir, "BREAKOUT_NOT_PERSISTENT", "[ENTRY][CRYPTO_FLAG][LATE_BLOCK]");

            bool fakeBreak =
                ctx.RangeFakeoutBars_M1 <= 2 ||
                (!ctx.Structure.ContinuationConfirmedSignal && !ctx.IsAtrExpanding_M5) ||
                (ctx.Structure.ImpulseStrength < MinImpulseStrength && !ctx.M1TriggerInTrendDirection);

            if (fakeBreak)
                return Reject(ctx, dir, "FAKE_BREAKOUT", "[ENTRY][CRYPTO_FLAG][FAKEBREAK_BLOCK]");

            ctx.Log?.Invoke("[ENTRY][CRYPTO_FLAG][PERSISTENCE_OK]");

            int score = 62;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.Structure.FlagCompression <= 0.40)
                score += 8;
            if (ctx.Structure.ContinuationConfirmedSignal)
                score += 8;
            score = Math.Max(0, Math.Min(100, score));

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = $"{SymbolTag}_FLAG_STRUCTURE_FIRST_OK"
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
