using System;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes.Crypto;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = 18;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 22;

            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", score);

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Block(ctx, "NO_CRYPTO_PROFILE", score);

            var bars = ctx.M5;
            if (bars == null || bars.Count < 20)
                return Block(ctx, "M5_NOT_READY", score);

            int lastClosed = bars.Count - 2;

            // =========================
            // DIRECTION
            // =========================
            TradeDirection dir = ctx.TrendDirection;

            // ===== CRYPTO STRICT CONTINUATION RULE =====
            if (dir == TradeDirection.None)
            {
                return Block(ctx, "CRYPTO_PULLBACK_NO_TREND", score);
            }
                
            // =========================
            // CRYPTO TREND ENERGY MINIMUM (CONTINUATION CORE)
            // =========================

            bool lowVol = !ctx.IsVolatilityAcceptable_Crypto;

            // ---- BASE ENERGY CHECK ----
            bool baseEnergyOk =
                ctx.Adx_M5 >= 23 &&
                ctx.AdxSlope_M5 > 0 &&
                (ctx.IsAtrExpanding_M5 || ctx.BarsSinceImpulse_M5 <= 3);

            // ---- LOW VOL STRICT MODE ----
            if (lowVol)
            {
                bool strictEnergyOk =
                    ctx.Adx_M5 >= 25 &&
                    ctx.HasImpulse_M5 &&
                    ctx.BarsSinceImpulse_M5 <= 3;

                if (!strictEnergyOk)
                {
                    return Block(ctx,
                        "CRYPTO_PULLBACK_LOWVOL_NO_ENERGY",
                        score,
                        dir);
                }
            }
            else
            {
                if (!baseEnergyOk)
                {
                    return Block(ctx,
                        "CRYPTO_PULLBACK_NO_TREND_ENERGY",
                        score,
                        dir);
                }
            }

            // =========================
            // EMA RECLAIM
            // =========================
            if (dir == TradeDirection.Short && lastClosed >= 1)
            {
                bool ema21Reclaim =
                    bars[lastClosed].Close > ctx.Ema21_M5 &&
                    bars[lastClosed - 1].Close <= ctx.Ema21_M5;

                if (ema21Reclaim)
                    score -= 8;
            }

            // =========================
            // IMPULSE TOO OLD HARD
            // =========================
            if (ctx.BarsSinceImpulse_M5 > 16)
                return Block(ctx, "IMPULSE_TOO_OLD", score, dir);

            // =========================
            // HIGH VOL WITHOUT IMPULSE HARD
            // =========================
            if (profile.BlockPullbackOnHighVolWithoutImpulse &&
                !ctx.IsVolatilityAcceptable_Crypto &&
                !ctx.HasImpulse_M5)
            {
                return Block(ctx, "CRYPTO_PULLBACK_VOL_BLOCK_NO_IMPULSE", score, dir);
            }
            
            // =========================
            // LOW VOL + NO CLEAN REACTION HARD
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto &&
                !ctx.IsPullbackDecelerating_M5 &&
                !ctx.HasReactionCandle_M5)
            {
                return Block(ctx,
                    "CRYPTO_PULLBACK_LOW_VOL_NO_REACTION",
                    score,
                    dir);
            }

            // =========================
            // PULLBACK TOO DEEP HARD
            // =========================
            if (ctx.PullbackDepthAtr_M5 > 1.8)
                return Block(ctx, "PULLBACK_TOO_DEEP", score, dir);

            // =========================
            // TREND FATIGUE SOFT
            // =========================
            bool trendFatigue =
                ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0 &&
                ctx.AtrSlope_M5 <= 0 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 8;

            if (trendFatigue)
                score -= 10;

            // =========================
            // ADX ROLL OVER CONTINUATION BLOCK
            // =========================
            bool continuation =
                ctx.BarsSinceImpulse_M5 > 2 &&
                !ctx.IsPullbackDecelerating_M5;

            if (continuation &&
                ctx.Adx_M5 >= 38 &&
                ctx.AdxSlope_M5 <= 0 &&
                !ctx.IsAtrExpanding_M5)
            {
                return Block(ctx,
                    "CRYPTO_PULLBACK_ADX_ROLL_OVER_CONT",
                    score,
                    dir);
            }

            // =========================
            // REQUIRE IMPULSE HARD
            // =========================
            if (!ctx.HasImpulse_M5)
            {
                score -= 6;   // soft penalty instead of hard block
            }

            // =========================
            // IMPULSE AGE SOFT
            // =========================
            if (ctx.BarsSinceImpulse_M5 > 3)
            {
                int agePenalty = Math.Min(8, (ctx.BarsSinceImpulse_M5 - 3) * 1);
                score -= agePenalty;

                Console.WriteLine(
                    $"[BTC_PULLBACK][SOFT] IMPULSE_AGE penalty={agePenalty} bars={ctx.BarsSinceImpulse_M5}"
                );
            }

            // =========================
            // VOL SOFT
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 4;

            // =========================
            // PULLBACK QUALITY
            // =========================
            bool validPullbackReaction =
                ctx.IsPullbackDecelerating_M5 &&
                ctx.HasReactionCandle_M5 &&
                ctx.LastClosedBarInTrendDirection;

            if (validPullbackReaction)
                score += 12;
            else
                score -= 8;   // ← erős minőségszűrés

            // =========================
            // M1 CONFIRM
            // =========================
            if (ctx.M1TriggerInTrendDirection)
                score += 8;
            else
                score -= 1;

            // =========================
            // IMPULSE BONUS
            // =========================
            if (ctx.BarsSinceImpulse_M5 <= 3 && validPullbackReaction)
                score += 6;

            // =========================
            // HTF SOFT PENALTY
            // =========================
            if (ctx.CryptoHtfAllowedDirection != TradeDirection.None &&
                ctx.CryptoHtfAllowedDirection != dir &&
                ctx.CryptoHtfConfidence01 > 0.0)
            {
                int htfPenalty = (int)Math.Round(2 + 6 * ctx.CryptoHtfConfidence01);
                score -= htfPenalty;
            }

            // =========================
            // HTF TRANSITION STRICT FILTER (SAFE)
            // =========================
            bool isTransition =
                ctx.CryptoHtfAllowedDirection == TradeDirection.None &&
                ctx.CryptoHtfConfidence01 > 0.0;

            if (isTransition)
            {
                if (!validPullbackReaction ||
                    ctx.BarsSinceImpulse_M5 > 4)
                {
                    return Block(ctx,
                        "CRYPTO_PULLBACK_TRANSITION_STRICT",
                        score,
                        dir);
                }

                score -= 4;
            }

            // =========================
            // OVEREXTENDED SOFT
            // =========================
            if (ctx.AtrM5 > 0)
            {
                double distAtr = Math.Abs(bars[lastClosed].Close - ctx.Ema21_M5) / ctx.AtrM5;
                if (distAtr > 0.9)
                    score -= 4;
            }

            // ===== COUNTER-TREND HARD BLOCK =====
            if (ctx.TrendDirection != TradeDirection.None &&
                dir != ctx.TrendDirection)
            {
                return Block(ctx, "CRYPTO_COUNTER_TREND_BLOCK", score, dir);
            }

            // =========================
            // FINAL CHECK
            // =========================
            if (score < MIN_SCORE)
                return Block(ctx, $"SCORE_TOO_LOW_{score}", score, dir);

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

        private EntryEvaluation Block(EntryContext ctx, string reason, int score, TradeDirection dir = TradeDirection.None)
        {
            Console.WriteLine(
                $"[BTC_PULLBACK][BLOCK] {reason} | dir={dir} | score={score}"
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
    }
}
