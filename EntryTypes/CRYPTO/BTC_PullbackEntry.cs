using System;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes.Crypto;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = 20;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 0; // 🔥 MINDEN ELÉ

            // =========================
            // CTX / SAFETY
            // =========================
            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", score);

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);

            // =========================
            // DIRECTION (must NOT be None, or TradeCore will drop it)
            // =========================
            var bars = ctx.M5;
            if (bars == null || bars.Count < 20)
                return Block(ctx, "M5_NOT_READY", score);

            int lastClosed = bars.Count - 2;

            TradeDirection dir = ctx.TrendDirection;
            bool usingSoftFallback = false;

            if (dir == TradeDirection.None)
            {
                // csak akkor blockolj, ha tényleg nincs semmi
                if (!ctx.HasImpulse_M5 || ctx.BarsSinceImpulse_M5 > 4)
                {
                    return Block(ctx, "NO_PULLBACK_DIRECTION", score);
                }

                // ATR expanzió ne legyen kötelező
                dir = (bars[lastClosed].Close >= ctx.Ema21_M5)
                    ? TradeDirection.Long
                    : TradeDirection.Short;
            }

            // =========================
            // EMA21 RECLAIM INVALIDATES SHORT PULLBACK
            // =========================
            if (dir == TradeDirection.Short)
            {
                bool ema21Reclaim =
                    bars[lastClosed].Close > ctx.Ema21_M5 &&
                    bars[lastClosed - 1].Close <= ctx.Ema21_M5;

                if (ema21Reclaim)
                {
                    // túl szigorú a hard block – legyen inkább soft
                    score -= 8; // 6–10 között jó, én 8-at tennék
                }
            }

            // =========================
            // IMPULSE GUARDS (FX STYLE)
            // =========================
            if (!ctx.HasImpulse_M5 && profile.RequireStrongImpulseForPullback)
                return Block(ctx, "NO_IMPULSE", score);

            if (ctx.BarsSinceImpulse_M5 > 16)
                return Block(ctx, "IMPULSE_TOO_OLD", score);

            if (profile.RequireStrongImpulseForPullback && ctx.BarsSinceImpulse_M5 > 3)
                return Block(ctx, "CRYPTO_PULLBACK_IMPULSE_NOT_FRESH", score);

            // =========================
            // BTC HIGH-VOL FAKE CONTINUATION
            // =========================
            if (profile.BlockPullbackOnHighVolWithoutImpulse &&
                !ctx.IsVolatilityAcceptable_Crypto &&
                ctx.BarsSinceImpulse_M5 > 2)
            {
                return Block(ctx, "CRYPTO_PULLBACK_VOLATILITY_BLOCK", score);
            }

            // =========================
            // PULLBACK DEPTH – HARD
            // =========================
            if (ctx.PullbackDepthAtr_M5 > 1.8)
                return Block(ctx, "PULLBACK_TOO_DEEP", score);

            // =========================
            // TREND FATIGUE ULTRASOUND
            // =========================
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
            {
                score -= 10; // nagy bünti, de ne hard stop
            }

            // =========================
            // BASE SCORE
            // =========================
            score = 25;

            // =========================
            // TREND ENERGY PROXY (ADX helyett)
            // =========================
            if (!ctx.IsAtrExpanding_M5 &&
                !ctx.IsVolatilityAcceptable_Crypto &&
                ctx.BarsSinceImpulse_M5 > 2)
            {
                score -= 4;
            }
            
            // =========================
            // VOL REGIME – SOFT
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 3;

            // =========================
            // CRYPTO EARLY PULLBACK GUARD (HARD)
            // =========================
            if (ctx.HasReactionCandle_M5 &&
                ctx.BarsSinceImpulse_M5 <= 2 &&
                !ctx.LastClosedBarInTrendDirection)
            {
                score -= 6;

                Console.WriteLine(
                    $"[BTC_PULLBACK][SOFT] CRYPTO_EARLY_PULLBACK_WAIT | " +
                    $"penalty=6 | BarsSinceImpulse={ctx.BarsSinceImpulse_M5}"
                );
            }

            // =========================
            // PULLBACK QUALITY
            // =========================
            bool validPullbackReaction =
                ctx.IsPullbackDecelerating_M5 &&
                ctx.HasReactionCandle_M5 &&
                ctx.LastClosedBarInTrendDirection;

            if (validPullbackReaction)
            {
                score += 10;
            }

            // =========================
            // M1 CONFIRMATION
            // =========================
            if (ctx.M1TriggerInTrendDirection)
                score += 8;
            else
                score -= 1;

            // =========================
            // IMPULSE BONUS
            // =========================
            if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 3 && validPullbackReaction)
                score += 8;

            // =====================================================
            // ====== XAU / INDEX MINTÁBÓL ÁTVETT SOFT KIEGÉSZÍTÉSEK
            // =====================================================

            // =========================
            // CHOP / RANGE – SOFT (INDEX)
            // =========================
            bool chopZone =
                ctx.Adx_M5 < 20 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 7 &&
                !ctx.IsAtrExpanding_M5;

            if (chopZone && ctx.Adx_M5 < 14)
                score -= 4;

            // =========================
            // HTF SOFT PENALTY (XAU)
            // =========================
            if (ctx.MetalHtfAllowedDirection != TradeDirection.None &&
                ctx.MetalHtfAllowedDirection != dir &&
                ctx.MetalHtfConfidence01 > 0)
            {
                int htfPenalty = (int)(2 + 5 * ctx.MetalHtfConfidence01);
                score -= htfPenalty;
            }

            // =========================
            // OVEREXTENDED – SOFT (XAU)
            // =========================
            double distFromEma = Math.Abs(bars[lastClosed].Close - ctx.Ema21_M5);
            double distAtr = distFromEma / ctx.AtrM5;

            if (distAtr > 0.9)
                score -= 4;

            // =========================
            // FINAL CHECK
            // =========================
            if (score < MIN_SCORE)
                return Block(ctx, $"SCORE_TOO_LOW_{score}", score);

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

        // 4 paraméteres – amikor már van dir
        private EntryEvaluation Block(EntryContext ctx, string reason, int score, TradeDirection dir)
        {
            Console.WriteLine(
                $"[BTC_PULLBACK][BLOCK] {reason} | dir={dir} | " +
                $"score={score}"
            );

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_Pullback,
                Direction = dir,
                IsValid = false,
                Score = score,
                Reason = reason
            };
        }

        // 3 paraméteres – backward compatibility
        private EntryEvaluation Block(EntryContext ctx, string reason, int score)
        {
            return Block(ctx, reason, score, TradeDirection.None);
        }
    }
}
