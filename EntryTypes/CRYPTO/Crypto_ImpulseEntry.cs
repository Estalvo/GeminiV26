using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public class Crypto_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Impulse;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            if (!ctx.IsVolatilityAcceptable_Crypto)
                return Invalid(ctx, "CRYPTO_VOL_DISABLED");

            if (ctx.ImpulseDirection == TradeDirection.None && ctx.TrendDirection == TradeDirection.None)
                return Invalid(ctx, "NO_DIRECTION");

            TradeDirection dir =
                ctx.ImpulseDirection != TradeDirection.None ? ctx.ImpulseDirection : ctx.TrendDirection;

            int score = 60;
            bool breakoutDetected = ctx.HasImpulse_M1 || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            bool strongCandle = ctx.LastClosedBarInTrendDirection;
            bool followThrough = breakoutDetected || ctx.IsAtrExpanding_M5;

            score = TriggerScoreModel.Apply(ctx, $"CRYPTO_IMPULSE_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_IMPULSE_TRIGGER");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CRYPTO_IMPULSE dir={dir} score={score}"
            };
        }

        private EntryEvaluation Invalid(EntryContext ctx, string reason)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
    }
}
