using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_RangeBreakoutEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_RangeBreakout;
        private const int MIN_SCORE = 35;
        private const int MIN_RANGE_BARS = 15;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            // =========================
            // HARD GUARDS
            // =========================
            if (!ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            int score = 25;

            // =========================
            // VOL REGIME – SOFT
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto)
            {
                score -= 15;   // SOFT penalty, NEM return
            }

            var crypto = CryptoInstrumentMatrix.Get(ctx.Symbol);

            if (!crypto.AllowRangeBreakout)
                return Invalid(ctx, "DISABLED");

            if (!ctx.IsRange_M5 || ctx.RangeBarCount_M5 < MIN_RANGE_BARS)
                return Invalid(ctx, "NO_RANGE");

            TradeDirection dir = ctx.RangeBreakDirection;
            if (dir == TradeDirection.None)
                return Invalid(ctx, "NO_BREAK_DIR");

            var eval = NewEval(ctx, dir);

            // =========================
            // BREAK STRENGTH
            // =========================
            if (ctx.RangeBreakAtrSize_M5 > 1.2)
                return Invalid(ctx, "OVEREXTENDED_BREAK");

            score += 10;

            // =========================
            // FAKEOUT CHECK
            // =========================
            if (ctx.RangeFakeoutBars_M1 <= 1)
                score += 10;
            else
                score -= 15;

            // =========================
            // M1 CONFIRMATION (BONUS ONLY)
            // =========================
            if (ctx.M1TriggerInTrendDirection)
                score += 6;

            // =========================
            // ATR BEHAVIOUR
            // =========================
            if (ctx.IsAtrExpanding_M5)
                score += 10;

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"LowScore({score});";

            return eval;
        }

        private static EntryEvaluation NewEval(EntryContext ctx, TradeDirection dir)
            => new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = dir
            };

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_RangeBreakout,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason + ";"
            };
    }
}
