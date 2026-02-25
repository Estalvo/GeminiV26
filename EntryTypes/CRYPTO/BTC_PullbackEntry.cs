using System;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes.Crypto;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {        
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = 34;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 38;

            void ScoreLog(string label, int delta, int current)
            {
                Console.WriteLine(
                    $"[BTC_PULLBACK][SCORE] {label} Δ={delta} → {current}"
                );
            }

            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", score);

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Block(ctx, "NO_CRYPTO_PROFILE", score);

            var bars = ctx.M5;
            if (bars == null || bars.Count < 20)
                return Block(ctx, "M5_NOT_READY", score);

            int lastClosed = bars.Count - 2;
            
            TradeDirection dir = ctx.TrendDirection;
            
            if (dir != TradeDirection.Long && dir != TradeDirection.Short)
                return Block(ctx, "NO_TREND_DIR", score);

                // =========================
                // HTF DIRECTION OVERRIDE (so TC doesn't kill everything)
                // =========================
                var allow = ctx.CryptoHtfAllowedDirection;
                var htfConf = ctx.CryptoHtfConfidence01;

                // csak erős HTF esetén igazodunk hozzá
                if (allow != TradeDirection.None && dir != allow)
                {
                    // ha HTF domináns → igazodunk
                    if (htfConf >= 0.7)
                    {
                        dir = allow;
                        Console.WriteLine($"[BTC_PULLBACK][HTF_DIR_OVERRIDE] conf={htfConf:0.00} dir-> {dir}");
                    }
                    else
                    {
                        // ha nem domináns → ne adjunk vissza ellentétes irányt
                        // hanem blokkoljuk itt
                        return Block(ctx,
                            $"HTF_SOFT_CONFLICT conf={htfConf:0.00}",
                            score,
                            dir);
                    }
                }

            // =========================
            // IMPULSE DIRECTION LOCK (CRYPTO SAFETY)
            // =========================

            TradeDirection lastClosedDir = TradeDirection.None;
            if (lastClosed >= 0)
            {
                var c = bars[lastClosed];
                if (c.Close > c.Open) lastClosedDir = TradeDirection.Long;
                else if (c.Close < c.Open) lastClosedDir = TradeDirection.Short;
            }

            // "Strong impulse" proxy (ha nincs külön flag)
            // - HasImpulse_M5 már megvan nálad
            // - a veszélyes ablak: friss impulse után 1-2 bar
            bool freshImpulse = ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 2;

            if (freshImpulse && lastClosedDir != TradeDirection.None)
            {
                // 1) Counter-trend lock (ez fogja meg a squeeze-t)
                if (dir != lastClosedDir)
                {
                    return Block(ctx,
                        $"IMPULSE_LOCK_CT dir={dir} lastClosedDir={lastClosedDir} barsSinceImpulse={ctx.BarsSinceImpulse_M5}",
                        score,
                        dir);
                }

                // 2) Cooldown: ne nyiss azonnal új PB-t impulse után (whipsaw védelem)
                if (ctx.BarsSinceImpulse_M5 <= 1 &&
                    (!ctx.IsPullbackDecelerating_M5 || !ctx.HasReactionCandle_M5))
                {
                    return Block(ctx,
                        $"IMPULSE_COOLDOWN barsSinceImpulse={ctx.BarsSinceImpulse_M5} pbDecel={ctx.IsPullbackDecelerating_M5} react={ctx.HasReactionCandle_M5}",
                        score,
                        dir);
                }
            }

            // =========================
            // BREAKOUT IMPULSE BLOCK
            // =========================

            bool breakoutImpulse =
                freshImpulse &&
                ctx.AtrM5 > 0 &&
                Math.Abs(bars[lastClosed].Close - bars[lastClosed].Open) > 1.2 * ctx.AtrM5;

            if (breakoutImpulse && ctx.BarsSinceImpulse_M5 <= 3)
            {
                return Block(ctx,
                    $"PB_BLOCK_AFTER_BREAKOUT_IMPULSE body>1.2ATR bars={ctx.BarsSinceImpulse_M5}",
                    score,
                    dir);
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
                    // LowVol ne legyen hard block
                    score -= 6;
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
            // COMPOSITE MOMENTUM FUEL INDEX (NEW)
            // =========================

            int fuelScore = 0;

            // ADX slope component
            if (ctx.AdxSlope_M5 > 0.5)
                fuelScore += 6;
            else if (ctx.AdxSlope_M5 > 0)
                fuelScore += 3;
            else if (ctx.AdxSlope_M5 < -0.3)
                fuelScore -= 6;
            else
                fuelScore -= 3;

            // ATR expansion component
            if (ctx.IsAtrExpanding_M5 && ctx.AtrSlope_M5 > 0)
                fuelScore += 6;
            else if (!ctx.IsAtrExpanding_M5 && ctx.AtrSlope_M5 <= 0)
                fuelScore -= 6;
            else
                fuelScore -= 2;

            // Impulse freshness
            if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 2 && dir == lastClosedDir)
                fuelScore += 5;
            else if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 <= 2 && dir != lastClosedDir)
                fuelScore -= 8; // CT impulse büntetés

            // Pullback quality as energy signal
            if (ctx.IsPullbackDecelerating_M5 && ctx.HasReactionCandle_M5)
                fuelScore += 4;
            else
                fuelScore -= 3;

            // Low volatility scaling
            if (!ctx.IsVolatilityAcceptable_Crypto)
                fuelScore -= 4;

            // =========================
            // FUEL INTEGRATION
            // =========================

            // Hard exhaustion block
            if (fuelScore <= -12)
            {
                return Block(ctx,
                    "CRYPTO_PULLBACK_NO_FUEL",
                    score,
                    dir);
            }

            // Soft scaling
            if (fuelScore < 0)
            {
                int penalty = (int)Math.Round(Math.Abs(fuelScore) * 0.8);
                score -= penalty;
            }
            else if (fuelScore > 8)
            {
                score += 4;
            }

            Console.WriteLine($"[BTC_PULLBACK][FUEL_APPLIED] fuel={fuelScore} newScore={score}");
            Console.WriteLine($"[BTC_PULLBACK][FUEL] adxSlope={ctx.AdxSlope_M5:0.00} atrSlope={ctx.AtrSlope_M5:0.00} impulse={ctx.HasImpulse_M5} fuel={fuelScore}");

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
            if (ctx.PullbackDepthAtr_M5 > 2.2)
                return Block(ctx, "PULLBACK_TOO_DEEP_EXTREME", score, dir);

            if (ctx.PullbackDepthAtr_M5 > 1.6)
                score -= 6;

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
            // ULTRA SOFT LATE TREND MICRO PENALTY
            // =========================
            bool ultraLateTrend =
                ctx.Adx_M5 >= 50 &&
                ctx.AdxSlope_M5 <= 0.2 &&
                ctx.AtrSlope_M5 <= 0;

            if (ultraLateTrend)
            {
                score -= 2;   // extrém finom korrekció
            }
            
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
                int raw = ctx.BarsSinceImpulse_M5 - 5;
                int agePenalty = Math.Max(0, Math.Min(3, raw));
                
                score -= agePenalty;

                Console.WriteLine($"[BTC_PULLBACK][SOFT] IMPULSE_AGE penalty={agePenalty} bars={ctx.BarsSinceImpulse_M5}");
            }

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
            // OVEREXTENDED SOFT
            // =========================
            if (ctx.AtrM5 > 0)
            {
                double distAtr = Math.Abs(bars[lastClosed].Close - ctx.Ema21_M5) / ctx.AtrM5;
                if (distAtr > 0.9)
                    score -= 4;
            }

            // =========================
            // UNIFIED HTF WEIGHTING
            // =========================

            bool htfConflict =
                ctx.CryptoHtfAllowedDirection != TradeDirection.None &&
                ctx.CryptoHtfAllowedDirection != dir;

            htfConf = ctx.CryptoHtfConfidence01;

            if (htfConflict && htfConf > 0)
            {
                // lineáris büntetés
                int htfPenalty = (int)Math.Round(3 + 10 * htfConf);
                score -= htfPenalty;

                Console.WriteLine($"[BTC_PULLBACK][HTF_PENALTY] conf={htfConf:0.00} penalty={htfPenalty}");

                // domináns HTF + gyenge M5 → hard block
                bool strongHtf = htfConf >= 0.7;
                bool weakLocal =
                    ctx.Adx_M5 < 28 ||
                    !validPullbackReaction ||
                    fuelScore < 4;

                if (strongHtf && weakLocal)
                {
                    return Block(ctx,
                        $"HTF_DOMINANT_BLOCK conf={htfConf:0.00}",
                        score,
                        dir);
                }
            }

            // =========================
            // LOW QUALITY ZONE FILTER
            // =========================

            bool grayZone =
                score < 34 &&
                (
                    !validPullbackReaction ||
                    ctx.Adx_M5 < 24 ||
                    !ctx.IsVolatilityAcceptable_Crypto
                );

            if (grayZone)
            {
                // SOFT gray-zone: ne hard reject, csak tolja le a score-t
                score -= 4;
                Console.WriteLine($"[BTC_PULLBACK][SOFT] GRAY_ZONE(-4) score={score} validPb={validPullbackReaction} adx={ctx.Adx_M5:0.0} volOk={ctx.IsVolatilityAcceptable_Crypto}");
            }

            int dynamicMinScore = MIN_SCORE;

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
            Console.WriteLine($"[BTC_PULLBACK][BLOCK] {reason} | dir={dir} | score={score}");

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
