using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using System;

namespace GeminiV26.EntryTypes.Crypto
{
    public class BTC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Crypto_Flag;

        private const int MaxBarsSinceImpulse = 22;
        private const int MinFlagBars = 3;
        private const int MaxFlagBars = 9;

        private const double BreakBufferAtr = 0.03;
        private const double MaxDistFromEmaAtr = 1.2;

        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Invalid(ctx, "CTX_NOT_READY");

            TradeDirection logicBiasDirection = ctx.LogicBiasDirection;
            if (logicBiasDirection == TradeDirection.None)
                return Invalid(ctx, TradeDirection.None, "NO_LOGIC_BIAS", 0);

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, "ATR_ZERO");

            int directionalBarsSinceImpulse = logicBiasDirection switch
            {
                TradeDirection.Long => ctx.BarsSinceImpulseLong_M5,
                TradeDirection.Short => ctx.BarsSinceImpulseShort_M5,
                _ => ctx.BarsSinceImpulse_M5
            };

            bool hasDirectionalImpulse = directionalBarsSinceImpulse <= MaxBarsSinceImpulse;

            if (!ctx.HasImpulse_M5 && !hasDirectionalImpulse && ctx.BarsSinceImpulse_M5 > 18)
                return Invalid(ctx, "NO_RECENT_IMPULSE");

            if (directionalBarsSinceImpulse > MaxBarsSinceImpulse)
                return Invalid(ctx, "LATE_FLAG");

            var bars = ctx.M5;
            int lastClosed = bars.Count - 2;
            int flagEnd = lastClosed - 1;

            int bestStart = -1;
            double bestRange = double.MaxValue;

            for (int len = MinFlagBars; len <= MaxFlagBars; len++)
            {
                int start = flagEnd - len + 1;
                if (start < 2)
                    continue;

                double hiTmp = double.MinValue;
                double loTmp = double.MaxValue;

                for (int i = start; i <= flagEnd; i++)
                {
                    double bodyHigh = Math.Max(bars[i].Open, bars[i].Close);
                    double bodyLow = Math.Min(bars[i].Open, bars[i].Close);

                    hiTmp = Math.Max(hiTmp, bodyHigh);
                    loTmp = Math.Min(loTmp, bodyLow);
                }

                double r = hiTmp - loTmp;

                if (r < bestRange)
                {
                    bestRange = r;
                    bestStart = start;
                }
            }

            if (bestStart < 2)
                return Invalid(ctx, "NO_FLAG_WINDOW");

            int flagStart = bestStart;

            double hi = double.MinValue;
            double lo = double.MaxValue;

            for (int i = flagStart; i <= flagEnd; i++)
            {
                hi = Math.Max(hi, bars[i].High);
                lo = Math.Min(lo, bars[i].Low);
            }

            bool hasValidRange = hi > lo && hi > 0 && lo > 0;
            if (!hasValidRange)
                ctx.Log?.Invoke("[FLAG WARN] No valid range → fallback mode");

            double rangeAtr = hasValidRange && ctx.AtrM5 > 0
                ? (hi - lo) / ctx.AtrM5
                : 0;

            var profile = CryptoInstrumentMatrix.Get(ctx.Symbol);

            double htfConf = ctx.ResolveAssetHtfConfidence01();
            var htfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                htfConf >= 0.6 &&
                htfDir != TradeDirection.None &&
                logicBiasDirection != TradeDirection.None &&
                htfDir != logicBiasDirection;

            if (htfMismatch)
            {
                ctx.Log?.Invoke(
                    $"[CRYPTO][HTF_SOFT] mismatch allowed | dir={logicBiasDirection} htf={htfDir} conf={htfConf:0.00}");
            }

            if (logicBiasDirection == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Long, hi, lo, hasValidRange, rangeAtr, lastClosed, profile?.MaxFlagAtrMult ?? 0);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (logicBiasDirection == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, TradeDirection.Short, hi, lo, hasValidRange, rangeAtr, lastClosed, profile?.MaxFlagAtrMult ?? 0);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return Invalid(ctx, TradeDirection.None, "NO_LOGIC_BIAS", 0);
        }
        private EntryEvaluation EvaluateSide(
            EntryContext ctx,
            TradeDirection dir,
            double hi,
            double lo,
            bool hasValidRange,
            double rangeAtr,
            int lastIndex,
            double maxFlagAtr)
        {
            var bars = ctx.M5;
            var bar = bars[lastIndex];

            int score = 0;
            double triggerScore = 0;
            int baseScore;
            int scoreAfterRegime;
            int scoreAfterHtf;

            TradeDirection impulseDirection = ctx.ImpulseDirection;
            if (impulseDirection == TradeDirection.None)
                impulseDirection = dir;
            int barsSinceImpulse = Math.Max(0, ctx.BarsSinceImpulse_M5);
            bool isSameDirection = (dir == impulseDirection);
            bool shouldBlock = (barsSinceImpulse < 1) && !isSameDirection;
            ctx.Log?.Invoke(
                $"[CRYPTO][IMPULSE_GATE] entryType=Flag barsSinceImpulse={barsSinceImpulse} impulseDir={impulseDirection} entryDir={dir} sameDir={isSameDirection.ToString().ToLowerInvariant()} blocked={shouldBlock.ToString().ToLowerInvariant()}");
            if (shouldBlock)
                return Invalid(ctx, dir, "IMPULSE_LOCK_IMMEDIATE_COUNTER", score);

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            bool structuredPB =
                ctx.PullbackBars_M5 >= 1 &&
                ctx.IsPullbackDecelerating_M5;

            string flagState = hasFlag ? "OK" : "FLAG_WEAK_OR_FORMING";

            double close = bar.Close;
            double open = bar.Open;

            double distFromEma = Math.Abs(close - ctx.Ema21_M5);

            if (distFromEma <= ctx.AtrM5 * 0.6)
                score += 8;
            else if (distFromEma > ctx.AtrM5 * MaxDistFromEmaAtr)
                score -= 8;

            double buf = ctx.AtrM5 * BreakBufferAtr;

            bool bullBreak =
                hasValidRange &&
                close > hi + buf &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool bearBreak =
                hasValidRange &&
                close < lo - buf &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool bullReclaim =
                hasValidRange &&
                close > hi &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.HasReactionCandle_M5 &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool bearReclaim =
                hasValidRange &&
                close < lo &&
                ctx.LastClosedBarInTrendDirection &&
                ctx.HasReactionCandle_M5 &&
                ctx.HasImpulse_M5 &&
                ctx.BarsSinceImpulse_M5 <= 6;

            bool breakoutSignal =
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir) ||
                ctx.RangeBreakDirection == dir ||
                (dir == TradeDirection.Long
                    ? (ctx.FlagBreakoutUp || ctx.FlagBreakoutUpConfirmed)
                    : (ctx.FlagBreakoutDown || ctx.FlagBreakoutDownConfirmed));

            bool hasVolatility =
                ctx.IsAtrExpanding_M5;

            bool hasStructure =
                hasFlag || structuredPB;

            if (!hasStructure)
            {
                ctx.Log?.Invoke(
                    "[CRYPTO][STRUCT_FILTER] entryType=Flag reason=NO_FLAG_OR_STRUCTURED_PULLBACK blocked=false");
            }

            bool continuationSignal = breakoutSignal;

            bool longValid = bullBreak || bullReclaim || (dir == TradeDirection.Long && breakoutSignal);
            bool shortValid = bearBreak || bearReclaim || (dir == TradeDirection.Short && breakoutSignal);
            bool breakoutDetected = dir == TradeDirection.Long ? longValid : shortValid;
            bool strongCandle =
                (dir == TradeDirection.Long && close > open) ||
                (dir == TradeDirection.Short && close < open);

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            int impulseScore = 0;
            if (ctx.HasImpulse_M5)
            {
                if (ctx.BarsSinceImpulse_M5 <= 3)
                    impulseScore = 25;
                else if (ctx.BarsSinceImpulse_M5 <= 6)
                    impulseScore = 20;
                else if (ctx.BarsSinceImpulse_M5 <= MaxBarsSinceImpulse)
                    impulseScore = 14;
                else
                    impulseScore = 8;
            }
            else if (barsSinceImpulse <= MaxBarsSinceImpulse)
            {
                impulseScore = 10;
            }

            int pullbackScore = 0;
            if (hasStructure)
            {
                pullbackScore = 12;
                if (hasFlag)
                    pullbackScore += 5;
                if (structuredPB)
                    pullbackScore += 3;
            }

            int tightnessScore = 0;
            if (rangeAtr > 0)
            {
                if (rangeAtr >= 0.20 && rangeAtr <= 0.70)
                    tightnessScore = 15;
                else if (rangeAtr > 0.70 && rangeAtr <= 1.00)
                    tightnessScore = 10;
                else if (rangeAtr > 1.00 && rangeAtr <= 1.25)
                    tightnessScore = 6;
                else
                    tightnessScore = 3;

                if (maxFlagAtr > 0 && rangeAtr > maxFlagAtr)
                    tightnessScore = Math.Max(0, tightnessScore - 4);
            }

            int breakoutReadinessScore = 0;
            if (breakoutDetected)
                breakoutReadinessScore = 15;
            else if (continuationSignal || strongCandle)
                breakoutReadinessScore = 10;
            else if (hasValidRange)
            {
                double distToBreak = dir == TradeDirection.Long
                    ? Math.Max(0.0, hi - close)
                    : Math.Max(0.0, close - lo);
                double distToBreakAtr = ctx.AtrM5 > 0 ? distToBreak / ctx.AtrM5 : 2.0;
                if (distToBreakAtr <= 0.25)
                    breakoutReadinessScore = 12;
                else if (distToBreakAtr <= 0.60)
                    breakoutReadinessScore = 8;
                else
                    breakoutReadinessScore = 5;
            }

            int momentumConsistencyScore = 0;
            if (ctx.IsVolatilityAcceptable_Crypto)
                momentumConsistencyScore += 5;
            if (hasVolatility)
                momentumConsistencyScore += 3;
            if (ctx.LastClosedBarInTrendDirection)
                momentumConsistencyScore += 2;

            baseScore =
                impulseScore +
                pullbackScore +
                tightnessScore +
                breakoutReadinessScore +
                momentumConsistencyScore;

            if (!hasStructure || !hasValidRange || (maxFlagAtr > 0 && rangeAtr > (maxFlagAtr * 1.6)))
            {
                baseScore = Math.Min(baseScore, -5);
            }

            if (missingImpulse)
            {
                ctx.Log?.Invoke(
                    $"[FLAG] Missing impulse context symbol={ctx.Symbol} entry={EntryType.Crypto_Flag} baseScore={baseScore}");
            }

            score = baseScore;

            bool trendRegime =
                ctx.MarketState?.IsTrend == true ||
                (!ctx.IsRange_M5 && ctx.Adx_M5 >= 18.0);
            string regime = trendRegime ? "Trend" : "NonTrend";
            int regimeDelta = trendRegime ? +5 : -10;
            score += regimeDelta;
            scoreAfterRegime = score;
            ctx.Log?.Invoke(
                $"[CRYPTO][REGIME_ADJUST] regime={regime} delta={regimeDelta} scoreAfter={scoreAfterRegime}");

            var htfDirection = ctx.CryptoHtfAllowedDirection;
            bool htfAligned = htfDirection == TradeDirection.None || htfDirection == dir;
            int htfDelta = htfAligned ? +6 : -6;
            score += htfDelta;
            scoreAfterHtf = score;
            ctx.Log?.Invoke(
                $"[CRYPTO][HTF_SCORE] htf={htfDirection} logic={dir} delta={htfDelta} scoreAfter={scoreAfterHtf}");

            bool followThrough = continuationSignal;
            bool impulseDetected = ctx.HasImpulse_M5 || barsSinceImpulse <= MaxBarsSinceImpulse;
            bool continuationDetected = continuationSignal;
            bool pullbackDetected = structuredPB;
            bool flagCompressionDetected = hasFlag && hasValidRange && rangeAtr > 0 && rangeAtr <= 1.00;
            bool isHtfAlignedShort = htfAligned && dir == TradeDirection.Short;

            bool structureValid = hasStructure;
            if (isHtfAlignedShort)
            {
                structureValid =
                    impulseDetected &&
                    (continuationDetected || pullbackDetected || flagCompressionDetected);
                ctx.Log?.Invoke(
                    $"[CRYPTO][FLAG_STRUCTURE_RELAX] dir=Short htfAlign=true impulse={impulseDetected.ToString().ToLowerInvariant()} continuation={continuationDetected.ToString().ToLowerInvariant()} pullback={pullbackDetected.ToString().ToLowerInvariant()} compression={flagCompressionDetected.ToString().ToLowerInvariant()} finalStructure={structureValid.ToString().ToLowerInvariant()}");
            }

            if (!structureValid)
                return Invalid(ctx, dir, "INVALID_STRUCTURE", score);

            int scoreAfterStructure = score;

            bool momentumAligned =
                hasVolatility ||
                ctx.LastClosedBarInTrendDirection ||
                continuationDetected ||
                breakoutDetected;
            bool originalMomentumAligned = momentumAligned;
            if (isHtfAlignedShort)
            {
                momentumAligned = momentumAligned || impulseDetected;
                ctx.Log?.Invoke(
                    $"[CRYPTO][FLAG_MOMENTUM_OVERRIDE] originalMomentum={originalMomentumAligned.ToString().ToLowerInvariant()} impulseDetected={impulseDetected.ToString().ToLowerInvariant()} finalMomentum={momentumAligned.ToString().ToLowerInvariant()}");
            }

            if (!momentumAligned)
                return Invalid(ctx, dir, "INVALID_MOMENTUM", score);

            int scoreAfterMomentum = score;

            if (breakoutDetected)
                triggerScore += 1;

            if (strongCandle)
                triggerScore += 1;

            if (followThrough)
                triggerScore += 2;

            score += (int)Math.Round(triggerScore * 5);

            if (triggerScore == 0)
                score -= 15;

            bool minimalTrigger = breakoutDetected || strongCandle;
            if (!minimalTrigger)
                score -= 10;

            int originalEarlyBreakPenalty = !breakoutDetected ? -8 : 0;
            int appliedEarlyBreakPenalty = originalEarlyBreakPenalty;
            if (isHtfAlignedShort && appliedEarlyBreakPenalty < -5)
                appliedEarlyBreakPenalty = -5;
            score += appliedEarlyBreakPenalty;
            if (originalEarlyBreakPenalty != 0)
            {
                ctx.Log?.Invoke(
                    $"[CRYPTO][FLAG_EARLY_BREAK_ADJUST] originalPenalty={originalEarlyBreakPenalty} appliedPenalty={appliedEarlyBreakPenalty}");
            }

            if (score < 30 && baseScore >= 40)
                score = 30;

            if (isHtfAlignedShort && structureValid)
            {
                int baseFloorScore = score;
                score = Math.Max(score, 35);
                if (continuationDetected)
                    score = Math.Max(score, 40);
                ctx.Log?.Invoke(
                    $"[CRYPTO][FLAG_SCORE_FLOOR] baseScore={baseFloorScore} finalScore={score} htfAlign=true continuation={continuationDetected.ToString().ToLowerInvariant()}");
            }

            int scoreAfterPenalty = score;
            ctx.Log?.Invoke(
                $"[CRYPTO][FLAG_FINAL_SCORE] base={baseScore} afterStructure={scoreAfterStructure} afterMomentum={scoreAfterMomentum} afterPenalty={scoreAfterPenalty} final={score}");

            ctx.Log?.Invoke(
                $"[CRYPTO][FLAG_SCORE] base={baseScore} afterRegime={scoreAfterRegime} afterHtf={scoreAfterHtf} final={score}");

            ctx.Log?.Invoke(
                $"[TRIGGER SCORE] breakout={(breakoutDetected ? 1 : 0)} strong={(strongCandle ? 1 : 0)} follow={(followThrough ? 1 : 0)} total={triggerScore:F0} finalScore={score}");

            if (score < MinScore)
                return Invalid(ctx, dir, $"LOW_SCORE({score})", score);

            double finalHtfConf = ctx.ResolveAssetHtfConfidence01();
            var finalHtfDir = ctx.ResolveAssetHtfAllowedDirection();
            bool htfMismatch =
                finalHtfConf >= 0.6 &&
                finalHtfDir != TradeDirection.None &&
                dir != TradeDirection.None &&
                finalHtfDir != dir;
            ctx.Log?.Invoke(
                $"[CRYPTO][ENTRY_FINAL] dir={dir} score={score} htfMismatch={htfMismatch}");

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"CR_FLAG_V2 dir={dir} score={score} rangeATR={rangeAtr:F2} rangeState={(hasValidRange ? "OK" : "FLAG_RANGE_UNKNOWN")} flagState={flagState}"
            };
            ApplyCryptoSourceTrace(ctx, eval, dir);
            return eval;
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
            => Invalid(ctx, TradeDirection.None, reason, 0);

        private static EntryEvaluation Invalid(EntryContext ctx, TradeDirection dir, string reason, int score)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Crypto_Flag,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = reason
            };
            CryptoDirectionFallback.ApplyIfEligible(ctx, eval, reason);
            ApplyCryptoSourceTrace(ctx, eval, eval.Direction);
            return eval;
        }

        private static void ApplyCryptoSourceTrace(EntryContext ctx, EntryEvaluation evaluation, TradeDirection candidateDirection)
        {
            if (evaluation == null)
                return;

            var sourceAllowedDirection = ctx?.CryptoHtfAllowedDirection ?? TradeDirection.None;
            evaluation.HtfTraceSourceStage = "SOURCE";
            evaluation.HtfTraceSourceModule = "CRYPTO_ENTRY";
            evaluation.HtfTraceSourceState = ctx?.CryptoHtfReason ?? "N/A";
            evaluation.HtfTraceSourceAllowedDirection = sourceAllowedDirection;
            evaluation.HtfTraceSourceAlign = sourceAllowedDirection == candidateDirection;
            evaluation.HtfTraceSourceCandidateDirection = candidateDirection;
            evaluation.HtfConfidence01 = ctx?.CryptoHtfConfidence01 ?? 0.0;
        }

    }
}
