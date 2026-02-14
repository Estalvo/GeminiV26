using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes.Crypto;
using System;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = 28;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            // =====================================================
            // 1️⃣ CTX / SAFETY
            // =====================================================
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, "CTX_NOT_READY");

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);

            var bars = ctx.M5;
            if (bars == null || bars.Count < 20)
                return Invalid(ctx, "M5_NOT_READY");

            int lastClosed = bars.Count - 2;


            // =====================================================
            // 2️⃣ DIRECTION RESOLUTION
            // =====================================================
            TradeDirection dir = ctx.TrendDirection;
            bool usingSoftFallback = false;

            if (dir == TradeDirection.None)
            {
                if (!ctx.HasImpulse_M5 ||
                    ctx.BarsSinceImpulse_M5 > 2 ||
                    !ctx.IsAtrExpanding_M5)
                {
                    return Invalid(ctx, "NO_HARD_TREND_PULLBACK_BLOCK");
                }

                usingSoftFallback = true;

                dir = (bars[lastClosed].Close >= ctx.Ema21_M5)
                    ? TradeDirection.Long
                    : TradeDirection.Short;
            }


            // =====================================================
            // 3️⃣ TREND FATIGUE ULTRASOUND (LIFECYCLE FILTER)
            // =====================================================

            bool adxExhausted =
                ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0;

            bool atrContracting =
                ctx.AtrSlope_M5 <= 0;

            bool diConverging =
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 8;

            bool impulseStale =
                ctx.BarsSinceImpulse_M5 > 3 ||
                !ctx.HasImpulse_M5;

            bool trendFatigue =
                adxExhausted &&
                atrContracting &&
                diConverging &&
                impulseStale;

            if (trendFatigue)
                return Invalid(ctx, "CRYPTO_TREND_FATIGUE_ULTRASOUND");


            // =====================================================
            // 4️⃣ IMPULSE VALIDITY
            // =====================================================
            if (!ctx.HasImpulse_M5 && profile.RequireStrongImpulseForPullback)
                return Invalid(ctx, "NO_IMPULSE");

            if (ctx.BarsSinceImpulse_M5 > 16)
                return Invalid(ctx, "IMPULSE_TOO_OLD");

            if (profile.RequireStrongImpulseForPullback && ctx.BarsSinceImpulse_M5 > 3)
                return Invalid(ctx, "CRYPTO_PULLBACK_IMPULSE_NOT_FRESH");


            // =====================================================
            // 5️⃣ STRUCTURE VALIDATION
            // =====================================================

            // Pullback depth
            if (ctx.PullbackDepthAtr_M5 > 1.3)
                return Invalid(ctx, "PULLBACK_TOO_DEEP");

            // EMA reclaim short invalidation
            if (dir == TradeDirection.Short)
            {
                bool ema21Reclaim =
                    bars[lastClosed].Close > ctx.Ema21_M5 &&
                    bars[lastClosed - 1].Close <= ctx.Ema21_M5;

                if (ema21Reclaim)
                    return Invalid(ctx, "PULLBACK_BLOCKED_BY_EMA21_RECLAIM");
            }


            // =====================================================
            // 6️⃣ VOLATILITY REGIME BLOCK
            // =====================================================
            if (profile.BlockPullbackOnHighVolWithoutImpulse &&
                !ctx.IsVolatilityAcceptable_Crypto &&
                ctx.BarsSinceImpulse_M5 > 2)
            {
                return Invalid(ctx, "CRYPTO_PULLBACK_VOLATILITY_BLOCK");
            }


            // =====================================================
            // 7️⃣ EARLY PULLBACK GUARD
            // =====================================================
            if (ctx.HasReactionCandle_M5 &&
                ctx.BarsSinceImpulse_M5 <= 2 &&
                !ctx.LastClosedBarInTrendDirection)
            {
                return Invalid(ctx, "CRYPTO_EARLY_PULLBACK_WAIT");
            }


            // =====================================================
            // 8️⃣ SCORING
            // =====================================================
            int score = 25;

            // =========================
            // CHOP / RANGE SOFT GUARD
            // =========================
            bool chopZone =
                ctx.Adx_M5 < 22 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 6 &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone)
                score -= 5;

            // Energy proxy
            if (!ctx.IsAtrExpanding_M5 &&
                !ctx.IsVolatilityAcceptable_Crypto &&
                ctx.BarsSinceImpulse_M5 > 2)
            {
                score -= 10;
            }

            // Vol regime penalty
            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 10;

            // Pullback quality
            bool validPullbackReaction =
                ctx.IsPullbackDecelerating_M5 &&
                ctx.HasReactionCandle_M5 &&
                (
                    ctx.TrendDirection != TradeDirection.None
                        ? ctx.LastClosedBarInTrendDirection
                        : true
                );

            if (validPullbackReaction)
                score += 10;

            // M1 confirmation
            if (ctx.M1TriggerInTrendDirection)
                score += 8;
            else
                score -= 3;

            // Fresh impulse bonus
            if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 3)
                score += 8;


            // =====================================================
            // 9️⃣ FINAL CHECK
            // =====================================================
            if (score < MIN_SCORE)
                return Invalid(ctx, $"SCORE_TOO_LOW_{score}");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Pullback,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"BTC_PULLBACK_OK dir={dir} score={score}"
            };
        }

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
