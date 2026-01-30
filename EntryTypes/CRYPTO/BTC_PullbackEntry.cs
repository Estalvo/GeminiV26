using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = 35;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            // =========================
            // CTX / SAFETY
            // =========================
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            // =========================
            // TREND DIRECTION
            // =========================
            TradeDirection dir = ctx.TrendDirection;
            if (dir == TradeDirection.None)
                return Invalid(ctx, "NO_TREND");
                        
            // =========================
            // IMPULSE / TIME GUARDS
            // =========================
            if (!ctx.HasImpulse_M5)
                return Invalid(ctx, "NO_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > 6)
                return Invalid(ctx, "IMPULSE_TOO_OLD");

            // =========================
            // PULLBACK DEPTH – HARD
            // =========================
            if (ctx.PullbackDepthAtr_M5 > 1.0)
                return Invalid(ctx, "PULLBACK_TOO_DEEP");

            // =========================
            // BASE SCORE
            // =========================
            int score = 25;

            // =========================
            // VOL REGIME – SOFT (CRYPTO)
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 10;

            // =========================
            // PULLBACK QUALITY
            // =========================
            // ideális pullback zóna
            if (ctx.PullbackDepthAtr_M5 >= 0.4 && ctx.PullbackDepthAtr_M5 <= 0.9)
                score += 15;

            // lassulás + reakció
            if (ctx.IsPullbackDecelerating_M5 &&
                ctx.HasReactionCandle_M5 &&
                ctx.LastClosedBarInTrendDirection)
            {
                score += 10;
            }

            // =========================
            // M1 CONFIRMATION (SOFT)
            // =========================
            if (ctx.M1TriggerInTrendDirection)
                score += 8;
            else
                score -= 3;

            // =========================
            // IMPULSE BONUS (SOFT)
            // =========================
            score += 8; // HasImpulse_M5 már validálva

            // =========================
            // ATR BEHAVIOUR (NEUTRAL)
            // =========================
            if (ctx.IsAtrExpanding_M5)
                score += 2;

            // =========================
            // FINAL CHECK
            // =========================
            if (score < MIN_SCORE)
                return Invalid(ctx, $"SCORE_TOO_LOW_{score}");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Pullback,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"BTC_PULLBACK_OK score={score}"
            };
        }

        private static EntryEvaluation NewEval(EntryContext ctx, TradeDirection dir)
            => new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Pullback,
                Direction = dir
            };

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_Pullback,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason + ";"
            };
    }
}
