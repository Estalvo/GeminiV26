using System;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes.Crypto;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = 28;

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
                ctx.Adx_M5 >= profile.MinAdxForPullback &&
                (
                    ctx.AdxSlope_M5 >= profile.MinAdxSlopeForPullback ||
                    ctx.IsAtrExpanding_M5 ||
                    ctx.BarsSinceImpulse_M5 <= 3
            );

            // ---- LOW VOL STRICT MODE ----
            if (lowVol)
            {
                bool strictEnergyOk =
                    ctx.Adx_M5 >= profile.MinAdxForPullback &&
                    (
                        ctx.HasImpulse_M5 ||
                        ctx.IsPullbackDecelerating_M5
                    );

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
            if (ctx.BarsSinceImpulse_M5 > profile.MaxBarsSinceImpulseForPullback)
            {
                // csak akkor hard block, ha energia is gyenge
                if (ctx.Adx_M5 < profile.MinAdxForPullback &&
                    !ctx.IsPullbackDecelerating_M5)
                {
                    return Block(ctx, "IMPULSE_TOO_OLD", score, dir);
                }

                // különben csak soft penalty
                score -= 4;
            }

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
                score -= 10;
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
            // LATE IMPULSE / EXPANSION EXHAUSTION BLOCK
            // =========================

            // 1️⃣ Többlépcsős impulzus detektálás
            bool lateImpulseStructure =
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 2 &&
                ctx.Adx_M5 >= 28;

            // 2️⃣ ADX már nem gyorsul
            bool adxStalling =
                ctx.Adx_M5 >= 30 &&
                ctx.AdxSlope_M5 <= 0;   // nem nő érdemben

            // 3️⃣ ATR nem tágul már
            bool atrNotExpanding =
                !ctx.IsAtrExpanding_M5 ||
                ctx.AtrSlope_M5 <= 0;

            // 4️⃣ Pullback valójában continuation (nincs valódi lassulás)
            bool noRealPullback =
                !ctx.IsPullbackDecelerating_M5 &&
                !ctx.HasReactionCandle_M5;

            // === HARD BLOCK ===
            if (lateImpulseStructure &&
                adxStalling &&
                atrNotExpanding &&
                noRealPullback)
            {
                return Block(ctx,
                    "CRYPTO_PULLBACK_LATE_IMPULSE_EXHAUSTION",
                    score,
                    dir);
            }
            
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
            if (!ctx.HasImpulse_M5 && ctx.Adx_M5 < profile.MinAdxForPullback)
            {
                score -= 4;
            }

            // =========================
            // IMPULSE AGE SOFT
            // =========================
            if (ctx.BarsSinceImpulse_M5 > 3)
            {
                int agePenalty = Math.Min(3, (ctx.BarsSinceImpulse_M5 - 5));
                score -= agePenalty;

                Console.WriteLine(
                    $"[BTC_PULLBACK][SOFT] IMPULSE_AGE penalty={agePenalty} bars={ctx.BarsSinceImpulse_M5}"
                );
            }

            // =========================
            // VOL SOFT
            // =========================
            if (!ctx.IsVolatilityAcceptable_Crypto)
                score -= 2;

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
                score -= 3;   // ← erős minőségszűrés

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
                if (!validPullbackReaction)
                    score -= 6;

                if (ctx.BarsSinceImpulse_M5 > 5)
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
            
            // =========================
            // COUNTER-TREND SOFT PENALTY
            // =========================
            if (ctx.CryptoHtfAllowedDirection != TradeDirection.None &&
                ctx.CryptoHtfAllowedDirection != dir)
            {
                int ctPenalty = (int)Math.Round(6 + 10 * ctx.CryptoHtfConfidence01);
                score -= ctPenalty;

                Console.WriteLine(
                    $"[BTC_PULLBACK][SOFT_COUNTER_TREND] penalty={ctPenalty} htfConf={ctx.CryptoHtfConfidence01:0.00}"
                );
            }

            // =========================
            // HTF DIRECTION CONFLICT BOOST
            // =========================

            int dynamicMinScore = MIN_SCORE;

            bool htfConflict =
                ctx.CryptoHtfAllowedDirection != TradeDirection.None &&
                ctx.CryptoHtfAllowedDirection != dir;

            if (htfConflict)
            {
                int boost = (int)Math.Round(4 + 10 * ctx.CryptoHtfConfidence01);
                dynamicMinScore += boost;

                Console.WriteLine(
                    $"[BTC_PULLBACK][HTF_CONFLICT] boost={boost} newMin={dynamicMinScore} htfConf={ctx.CryptoHtfConfidence01:0.00}"
                );
            }

            // =========================
            // FINAL CHECK
            // =========================
            if (score < dynamicMinScore)
                return Block(ctx, $"SCORE_TOO_LOW_{score}_MIN_{dynamicMinScore}", score, dir);

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
