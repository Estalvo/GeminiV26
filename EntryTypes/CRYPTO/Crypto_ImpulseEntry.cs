using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.Crypto
{
    public class Crypto_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Impulse;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            return null; // WEEKEND: disable crypto impulse entry
        }

        /*
        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            // =========================
            // HARD GUARDS
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto)
                return null;

            if (!ctx.HasImpulse_M1)
                return null;

            if (ctx.ImpulseDirection == TradeDirection.None)
                return null;

            if (ctx.TrendDirection == TradeDirection.None)
                return null;

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = ctx.ImpulseDirection,
                Score = 60,
                Reason = "CRYPTO_IMPULSE_FILTERED"
            };
        }*/
    }
}
