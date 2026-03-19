using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes.Crypto;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Pullback;
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;
        private const int BiasAgainstPenalty = 8;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", 36);

            var originalTrendDirection = ctx.TrendDirection;
            try
            {
                var longEval = EvaluateDirectional(ctx, TradeDirection.Long);
                var shortEval = EvaluateDirectional(ctx, TradeDirection.Short);

                return EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval);
            }
            finally
            {
                ctx.TrendDirection = originalTrendDirection;
            }
        }

        private EntryEvaluation EvaluateDirectional(EntryContext ctx, TradeDirection forcedDirection)
        {
            int score = 36;
            int setupScore = 0;

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
            var bar = bars[lastClosed];

            var originalTrendDir = ctx.TrendDirection;
            ctx.TrendDirection = forcedDirection;

            TradeDirection dir = forcedDirection;

            // =========================
            // HTF DIRECTIONAL BIAS (score-only)
            // =========================
            var allow = ctx.CryptoHtfAllowedDirection;
            var htfConf = ctx.CryptoHtfConfidence01;

            if (dir != TradeDirection.Long && dir != TradeDirection.Short)
                return Block(ctx, "NO_TREND_DIR", score);

            if (allow != TradeDirection.None && dir != allow)
            {
                const int SoftConflictPenalty = 4;
                score -= SoftConflictPenalty;
                Console.WriteLine(
                    $"[BTC_PULLBACK][HTF_SOFT_CONFLICT] conf={htfConf:0.00} allow={allow} keepDir={dir} scoreAdj=-{SoftConflictPenalty}"
                );
            }
            else if (allow == TradeDirection.None)
            {
                Console.WriteLine($"[BTC_PULLBACK][HTF_NEUTRAL] conf={htfConf:0.00} keepDir={dir}");
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
            // MIN PULLBACK MATURITY GUARD
            // =========================
            if (ctx.HasImpulse_M5)
            {
                // 0-1 bar után nincs belépés
                if (ctx.BarsSinceImpulse_M5 < 2)
                {
                    return Block(ctx,
                        $"PULLBACK_TOO_EARLY barsSinceImpulse={ctx.BarsSinceImpulse_M5}",
                        score,
                        dir);
                }

                // 2. barnál csak tiszta pullback reactionnel engedünk
                if (ctx.BarsSinceImpulse_M5 == 2 &&
                    (!ctx.IsPullbackDecelerating_M5 ||
                    !ctx.HasReactionCandle_M5 ||
                    !ctx.LastClosedBarInTrendDirection))
                {
                    return Block(ctx,
                        $"PULLBACK_NOT_MATURE barsSinceImpulse={ctx.BarsSinceImpulse_M5} pbDecel={ctx.IsPullbackDecelerating_M5} react={ctx.HasReactionCandle_M5} trendBar={ctx.LastClosedBarInTrendDirection}",
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
                Math.Abs(bars[lastClosed].Close - bars[lastClosed].Open) > 0.8 * ctx.AtrM5;

            if (breakoutImpulse && ctx.BarsSinceImpulse_M5 <= 3)
            {
                return Block(ctx,
                    $"PB_BLOCK_AFTER_BREAKOUT_IMPULSE body>0.8ATR bars={ctx.BarsSinceImpulse_M5}",
                    score,
                    dir);
            }

            // =========================
            // STRONG BIAS ALIGNMENT GUARD (NEW)
            // =========================

            if (originalTrendDir != TradeDirection.None &&
                dir != originalTrendDir)
            {
                double diSpread = Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5);
                bool dominantTrend = diSpread >= 10;   // institutional threshold

                bool strongMomentum =
                    ctx.Adx_M5 >= 28 &&
                    ctx.AdxSlope_M5 >= -0.2 &&   // stabilabb mint >=0
                    ctx.HasImpulse_M5 &&
                    ctx.BarsSinceImpulse_M5 <= 2 &&
                    dominantTrend;

                if (strongMomentum)
                {
                    return Block(ctx,
                        $"BIAS_STRONG_CONFLICT trend={originalTrendDir} dir={dir} adx={ctx.Adx_M5:0.0} diSpread={diSpread:0.0}",
                        score,
                        dir);
                }

                // ha nem erős momentum → marad a soft penalty
                score -= BiasAgainstPenalty;

                ctx.Log?.Invoke(
                    $"[BTC_PULLBACK][BIAS_SOFT_CONFLICT] trend={originalTrendDir} dir={dir} penalty={BiasAgainstPenalty}"
                );
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

            bool diCompression =
                ctx.Adx_M5 >= 38 &&
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) < 6;

            if (diCompression)
                score -= 6;

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
                ctx.BarsSinceImpulse_M5 > 3 &&
                !ctx.IsPullbackDecelerating_M5;

            bool trendDominant =
                Math.Abs(ctx.PlusDI_M5 - ctx.MinusDI_M5) >= 12;

            bool candleAligned =
                ctx.LastClosedBarInTrendDirection;

            if (continuation &&
                ctx.Adx_M5 >= 36 &&
                ctx.AdxSlope_M5 <= 0 &&
                !ctx.IsAtrExpanding_M5 &&
                !trendDominant &&
                !candleAligned)
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
            // REAL PULLBACK REQUIREMENT (INSTITUTIONAL GUARD)
            // =========================

            if (!ctx.IsPullbackDecelerating_M5 &&
                !ctx.HasReactionCandle_M5 &&
                ctx.BarsSinceImpulse_M5 <= 2)
            {
                return Block(ctx,
                    "NO_REAL_PULLBACK_AFTER_IMPULSE",
                    score,
                    dir);
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
                int htfPenalty = (int)Math.Round(3 + 10 * htfConf);
                score -= htfPenalty;

                bool weakLocal =
                    ctx.Adx_M5 < 28 ||
                    !validPullbackReaction ||
                    fuelScore < 4;

                if (htfConf >= 0.7 && weakLocal)
                    score -= 3;

                Console.WriteLine($"[BTC_PULLBACK][HTF_PENALTY] conf={htfConf:0.00} penalty={htfPenalty} weakLocal={weakLocal}");
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

            // ======================================
            // BTC PULLBACK QUALITY FILTER
            // ======================================
            bool isPullbackSetup = Type == EntryType.Crypto_Pullback;

            // 1) Minimum confidence (only for pullback)
            if (isPullbackSetup && score < MIN_SCORE)
            {
                Console.WriteLine($"[BTC FILTER] rejected: pullback low confidence {score}");
                ctx.Log?.Invoke($"[BTC FILTER] rejected: pullback low confidence {score}");
                return Block(ctx, "BTC_FILTER_PULLBACK_LOW_CONFIDENCE", score, dir);
            }

            // 2) Pullback requires impulse reclaim
            bool impulseReclaimConfirmed =
                ctx.HasImpulse_M5 &&
                ctx.LastClosedBarInTrendDirection;

            if (isPullbackSetup && !impulseReclaimConfirmed)
            {
                Console.WriteLine("[BTC FILTER] rejected: no impulse reclaim");
                ctx.Log?.Invoke("[BTC FILTER] rejected: no impulse reclaim");
                score -= 10;
            }

            // 3) Pullback timeout (dead pullback filter)
            if (isPullbackSetup && ctx.PullbackBars_M5 > 4)
            {
                Console.WriteLine($"[BTC FILTER] rejected: pullback timeout bars={ctx.PullbackBars_M5}");
                ctx.Log?.Invoke($"[BTC FILTER] rejected: pullback timeout bars={ctx.PullbackBars_M5}");
                return Block(ctx, $"BTC_FILTER_PULLBACK_TIMEOUT_BARS_{ctx.PullbackBars_M5}", score, dir);
            }

            // 4) ATR depth filter (reject overly deep pullbacks)
            if (isPullbackSetup)
            {
                double pullbackDepth = Math.Abs(ctx.PullbackDepthAtr_M5);

                if (pullbackDepth > 0.5)
                {
                    int compressionBars = Math.Max(0, Math.Min(ctx.PullbackBars_M5, 10));
                    int compressionStart = Math.Max(0, lastClosed - compressionBars + 1);

                    double compressionHigh = double.MinValue;
                    double compressionLow = double.MaxValue;

                    for (int i = compressionStart; i <= lastClosed; i++)
                    {
                        compressionHigh = Math.Max(compressionHigh, bars[i].High);
                        compressionLow = Math.Min(compressionLow, bars[i].Low);
                    }

                    double compressionRange = compressionHigh - compressionLow;
                    double atr = Math.Max(0, ctx.AtrM5);

                    bool compressionDetected =
                        compressionBars >= 3 &&
                        compressionBars <= 10 &&
                        compressionRange <= atr * 0.6;

                    if (!compressionDetected)
                    {
                        ctx.Log?.Invoke("[PB] rejected: deep pullback without compression");
                        score -= 10;
                    }

                    TradeDirection impulseDirection =
                        ctx.ImpulseDirection != TradeDirection.None ? ctx.ImpulseDirection : dir;

                    bool breakoutAligned =
                        (impulseDirection == TradeDirection.Long && bars[lastClosed].Close > compressionHigh) ||
                        (impulseDirection == TradeDirection.Short && bars[lastClosed].Close < compressionLow);

                    if (!breakoutAligned)
                    {
                        ctx.Log?.Invoke("[PB] rejected: breakout against impulse");
                        score -= 10;
                    }

                    ctx.Log?.Invoke("[PB] DeepPullbackContinuation accepted");
                }
            }

            // ======================================
            // BTC ASIA SESSION MODIFIER (crypto/BTC only)
            // ======================================
            bool isBtcSymbol =
                !string.IsNullOrWhiteSpace(ctx.Symbol) &&
                ctx.Symbol.IndexOf("BTC", StringComparison.OrdinalIgnoreCase) >= 0;

            bool isAsiaSession = ctx.Session == FxSession.Asia;

            if (isPullbackSetup && isBtcSymbol && isAsiaSession)
            {
                const int ASIA_MAX_PULLBACK_BARS = 3;
                const double ASIA_MAX_RETRACE_RATIO = 0.38;
                const int ASIA_MIN_CONFIDENCE = 48;

                Console.WriteLine("[BTC ASIA] detected: session modifier active");
                ctx.Log?.Invoke("[BTC ASIA] detected: session modifier active");

                int driftLookback = 5;
                int prevLookback = 5;

                bool hasWindowData = lastClosed >= (driftLookback + prevLookback - 1);
                bool diAligned =
                    dir == TradeDirection.Long
                        ? ctx.PlusDI_M5 >= ctx.MinusDI_M5
                        : ctx.MinusDI_M5 >= ctx.PlusDI_M5;
                bool slopeAligned =
                    dir == TradeDirection.Long
                        ? ctx.Ema21Slope_M5 >= 0
                        : ctx.Ema21Slope_M5 <= 0;
                bool impulseAligned =
                    ctx.ImpulseDirection == TradeDirection.None ||
                    ctx.ImpulseDirection == dir;

                bool progressionOk = false;
                int currStart = Math.Max(0, lastClosed - driftLookback + 1);
                int prevStart = Math.Max(0, currStart - prevLookback);
                int prevEnd = currStart - 1;

                if (hasWindowData && prevEnd >= prevStart)
                {
                    double currHigh = double.MinValue;
                    double currLow = double.MaxValue;
                    double prevHigh = double.MinValue;
                    double prevLow = double.MaxValue;

                    for (int i = currStart; i <= lastClosed; i++)
                    {
                        currHigh = Math.Max(currHigh, bars[i].High);
                        currLow = Math.Min(currLow, bars[i].Low);
                    }

                    for (int i = prevStart; i <= prevEnd; i++)
                    {
                        prevHigh = Math.Max(prevHigh, bars[i].High);
                        prevLow = Math.Min(prevLow, bars[i].Low);
                    }

                    progressionOk =
                        dir == TradeDirection.Long
                            ? (currHigh > prevHigh && currLow >= prevLow)
                            : (currLow < prevLow && currHigh <= prevHigh);
                }

                int flipCount = 0;
                int noiseStart = Math.Max(1, lastClosed - 5);
                for (int i = noiseStart; i <= lastClosed; i++)
                {
                    var prev = bars[i - 1];
                    var curr = bars[i];

                    bool prevBull = prev.Close >= prev.Open;
                    bool currBull = curr.Close >= curr.Open;
                    if (prevBull != currBull)
                        flipCount++;
                }

                bool choppy = flipCount >= 4;
                bool directionalDriftOk = diAligned && slopeAligned && impulseAligned && progressionOk && !choppy;

                if (!directionalDriftOk)
                {
                    Console.WriteLine("[BTC ASIA] rejected: no directional drift quality");
                    ctx.Log?.Invoke(
                        $"[BTC ASIA] rejected: no directional drift quality diAligned={diAligned} slopeAligned={slopeAligned} impulseAligned={impulseAligned} progression={progressionOk} flips={flipCount}"
                    );
                    return Block(ctx, "BTC_ASIA_NO_DIRECTIONAL_DRIFT_QUALITY", score, dir);
                }

                double retracementRatio = 0.0;
                if (ctx.HasImpulse_M5 && ctx.BarsSinceImpulse_M5 >= 0)
                {
                    int impulseStart = Math.Max(0, lastClosed - ctx.BarsSinceImpulse_M5);
                    double rangeHigh = double.MinValue;
                    double rangeLow = double.MaxValue;

                    for (int i = impulseStart; i <= lastClosed; i++)
                    {
                        rangeHigh = Math.Max(rangeHigh, bars[i].High);
                        rangeLow = Math.Min(rangeLow, bars[i].Low);
                    }

                    double impulseSize = Math.Max(0.0, rangeHigh - rangeLow);
                    double pullbackSize =
                        dir == TradeDirection.Long
                            ? Math.Max(0.0, rangeHigh - bars[lastClosed].Close)
                            : Math.Max(0.0, bars[lastClosed].Close - rangeLow);

                    if (impulseSize > 0)
                        retracementRatio = pullbackSize / impulseSize;
                }

                if (retracementRatio > ASIA_MAX_RETRACE_RATIO)
                {
                    Console.WriteLine($"[BTC ASIA] rejected: pullback too deep ratio={retracementRatio:0.00}");
                    ctx.Log?.Invoke($"[BTC ASIA] rejected: pullback too deep ratio={retracementRatio:0.00}");
                    return Block(ctx, "BTC_ASIA_PULLBACK_TOO_DEEP", score, dir);
                }

                if (ctx.PullbackBars_M5 > ASIA_MAX_PULLBACK_BARS)
                {
                    Console.WriteLine($"[BTC ASIA] rejected: pullback too slow bars={ctx.PullbackBars_M5}");
                    ctx.Log?.Invoke($"[BTC ASIA] rejected: pullback too slow bars={ctx.PullbackBars_M5}");
                    return Block(ctx, "BTC_ASIA_PULLBACK_TOO_SLOW", score, dir);
                }

                bool reclaimConfirmed =
                    ctx.LastClosedBarInTrendDirection &&
                    ctx.HasReactionCandle_M5 &&
                    impulseAligned;

                if (!reclaimConfirmed)
                {
                    Console.WriteLine("[BTC ASIA] rejected: no reclaim confirmation");
                    ctx.Log?.Invoke("[BTC ASIA] rejected: no reclaim confirmation");
                    score -= 10;
                }

                if (score < ASIA_MIN_CONFIDENCE)
                {
                    Console.WriteLine($"[BTC ASIA] rejected: low confidence score={score}");
                    ctx.Log?.Invoke($"[BTC ASIA] rejected: low confidence score={score}");
                    return Block(ctx, "BTC_ASIA_LOW_CONFIDENCE", score, dir);
                }

                Console.WriteLine(
                    $"[BTC ASIA] passed: drift={directionalDriftOk}, retrace={retracementRatio:0.00}, bars={ctx.PullbackBars_M5}, confidence={score}"
                );
                ctx.Log?.Invoke(
                    $"[BTC ASIA] passed: drift={directionalDriftOk}, retrace={retracementRatio:0.00}, bars={ctx.PullbackBars_M5}, confidence={score}"
                );
            }

            bool hasVolatility =
                ctx.IsAtrExpanding_M5;

            if (!hasVolatility)
                setupScore -= 30;

            bool hasFlag =
                dir == TradeDirection.Long
                    ? ctx.HasFlagLong_M5
                    : ctx.HasFlagShort_M5;

            bool structuredPB =
                ctx.PullbackBars_M5 >= 2 &&
                ctx.IsPullbackDecelerating_M5;

            bool hasStructure =
                hasFlag || structuredPB;

            if (!hasStructure)
                setupScore -= 30;
            else
                setupScore += 15;

            bool continuationSignal =
                ctx.M1TriggerInTrendDirection || validPullbackReaction;

            bool hasMomentum =
                continuationSignal;

            if (hasMomentum)
                setupScore += 20;

            bool breakoutDetected =
                ctx.M1TriggerInTrendDirection ||
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir);
            bool strongCandle = ctx.LastClosedBarInTrendDirection;
            bool followThrough = continuationSignal || validPullbackReaction;
            score = TriggerScoreModel.Apply(ctx, $"BTC_PULLBACK_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_PULLBACK_TRIGGER");
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, dynamicMinScore - 10);

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
