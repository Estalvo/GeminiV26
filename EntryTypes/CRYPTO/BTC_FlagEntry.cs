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

        private const int MaxBarsSinceImpulse = 18;
        private const int MinFlagBars = 3;
        private const int MaxFlagBars = 7;

        private const double BreakBufferAtr = 0.03;
        private const double MaxDistFromEmaAtr = 1.2;

        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;
        private const int HtfAgainstPenalty = 8;

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

            if (!ctx.HasImpulse_M5 && !hasDirectionalImpulse && ctx.BarsSinceImpulse_M5 > 14)
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

            if (ctx.ResolveAssetHtfConfidence01() >= 0.6 && ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None && ctx.ResolveAssetHtfAllowedDirection() != logicBiasDirection)
                return Invalid(ctx, logicBiasDirection, "HTF_MISMATCH", 0);

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
            int setupScore = 0;
            double triggerScore = 0;

            if (!ctx.HasImpulse_M5)
            {
                score -= 6;
            }
            else if (ctx.BarsSinceImpulse_M5 > 6)
            {
                score -= 4;
            }

            bool compression =
                ctx.AtrSlope_M5 <= 0.30 &&
                ctx.AdxSlope_M5 <= 1.5;

            if (!compression)
                score -= 6;

            if (rangeAtr > 0)
            {
                if (rangeAtr <= 0.8)
                {
                    score += 3;
                }
                else if (rangeAtr > 1.2)
                {
                    score -= 3;
                }

                if (rangeAtr < 0.15)
                    score -= 3;

                if (maxFlagAtr > 0 && rangeAtr > maxFlagAtr)
                    score -= 4;
            }
            else
            {
                score -= 2;
            }

            bool hasFlag =
                dir == TradeDirection.Long ? ctx.HasFlagLong_M5 :
                dir == TradeDirection.Short ? ctx.HasFlagShort_M5 :
                ctx.IsValidFlagStructure_M5;

            bool structuredPB =
                ctx.PullbackBars_M5 >= 2 &&
                ctx.IsPullbackDecelerating_M5;

            string flagState = hasFlag ? "OK" : "FLAG_WEAK_OR_FORMING";

            if (!hasFlag)
                score -= 2;

            if (ctx.IsVolatilityAcceptable_Crypto)
                score += 10;
            else
                score -= 10;

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

            if (!hasVolatility)
                setupScore -= 30;

            bool hasStructure =
                hasFlag || structuredPB;

            if (!hasStructure)
                setupScore -= 30;
            else
                setupScore += 15;

            bool continuationSignal = breakoutSignal;

            bool hasMomentum =
                continuationSignal;

            if (hasMomentum)
                setupScore += 20;

            bool longValid = bullBreak || bullReclaim || (dir == TradeDirection.Long && breakoutSignal);
            bool shortValid = bearBreak || bearReclaim || (dir == TradeDirection.Short && breakoutSignal);
            bool breakoutDetected = dir == TradeDirection.Long ? longValid : shortValid;
            bool strongCandle =
                (dir == TradeDirection.Long && close > open) ||
                (dir == TradeDirection.Short && close < open);

            if (strongCandle) score += 6;

            if (ctx.TrendDirection != TradeDirection.None &&
                dir != ctx.TrendDirection)
            {
                score -= HtfAgainstPenalty;
            }

            bool missingImpulse =
                string.Equals(ctx.Transition?.Reason, "MissingImpulse", StringComparison.Ordinal);

            if (missingImpulse)
            {
                ctx.Log?.Invoke(
                    "[FLAG] Missing impulse context" +
                    $"symbol={ctx.Symbol} entry={EntryType.Crypto_Flag} penalty=6 score={score}");
            }

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score += setupScore;

            bool followThrough = continuationSignal;

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

            if (!breakoutDetected)
                score -= 8;

            ctx.Log?.Invoke(
                $"[TRIGGER SCORE] breakout={(breakoutDetected ? 1 : 0)} strong={(strongCandle ? 1 : 0)} follow={(followThrough ? 1 : 0)} total={triggerScore:F0} finalScore={score}");

            if (setupScore <= 0)
                score = Math.Min(score, MinScore - 10);

            if (score < MinScore)
                return Invalid(ctx, dir, $"LOW_SCORE({score})", score);

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

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "BTC_FlagEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
