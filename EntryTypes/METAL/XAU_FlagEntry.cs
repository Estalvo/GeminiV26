using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core.Matrix;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Flag;

        private const int BarsNotReadyMin = 20;
        private const double MinBodyRatio = 0.55;

        private const int HtfAgainstPenalty = 5;

        private const double MinCloseBreakAtr = 0.05;
        private const double HtfAgainstMinBreakAtr = 0.08;
        private const double HtfAgainstMinBody = 0.60;

        private const int ScoreDeadband = 2;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < BarsNotReadyMin)
                return Invalid(ctx, "CTX_NOT_READY");

            if (ctx.LogicBias == TradeDirection.None)
                return InvalidDir(ctx, TradeDirection.None, "NO_LOGIC_BIAS", 0);

            var matrix = ctx.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Invalid(ctx, "SESSION_DISABLED");

            if (!TryResolveSession(ctx.Session, out var session, out var tag))
                return Invalid(ctx, "NO_SESSION");

            var profile = XAU_InstrumentMatrix.Get(ctx.Symbol);
            if (profile == null)
                return Invalid(ctx, "NO_PROFILE");

            if (!profile.FlagTuning.TryGetValue(session, out var tuning))
                return Invalid(ctx, "NO_TUNING");

            double hi = ctx.FlagHigh;
            double lo = ctx.FlagLow;

            var bars = ctx.M5;
            int last = bars.Count - 2;
            var bar = bars[last];

            ctx.Log?.Invoke($"[FLAG DEBUG] hi={ctx.FlagHigh} lo={ctx.FlagLow} atr={ctx.AtrM5} range={(ctx.FlagHigh - ctx.FlagLow)}");
            ctx.Log?.Invoke($"[FLAG DEBUG] hasFlagLong={ctx.HasFlagLong_M5} hasFlagShort={ctx.HasFlagShort_M5}");

            bool hasValidRange = hi > lo && hi > 0 && lo > 0;
            ctx.Log?.Invoke($"[FLAG RANGE CHECK] hi={hi} lo={lo} valid={hasValidRange}");

            if (!hasValidRange)
                return Invalid(ctx, "NO_FLAG_RANGE");

            if (!ctx.HasFlagLong_M5 && !ctx.HasFlagShort_M5)
                return Invalid(ctx, "NO_FLAG_STRUCTURE");

            double rangeAtr = hasValidRange && ctx.AtrM5 > 0
                ? (hi - lo) / ctx.AtrM5
                : 0;

            if (hasValidRange && rangeAtr > tuning.MaxFlagAtrMult * 1.3)
                ctx.Log?.Invoke($"[FLAG WARN] Wide range kept as soft signal rangeAtr={rangeAtr:F2} maxHard={tuning.MaxFlagAtrMult * 1.3:F2}");

            if (hasValidRange)
            {
                bool wickBoth = bar.High >= hi && bar.Low <= lo;
                bool closeUp = bar.Close > hi;
                bool closeDn = bar.Close < lo;

                if (wickBoth && !closeUp && !closeDn)
                    return Invalid(ctx, "AMBIGUOUS_SPIKE");
            }

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
                return InvalidDir(ctx, TradeDirection.None, "HTF_MISMATCH", 0);

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(TradeDirection.Long, ctx, tuning, hi, lo, hasValidRange, rangeAtr, bar, last);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(TradeDirection.Short, ctx, tuning, hi, lo, hasValidRange, rangeAtr, bar, last);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return InvalidDir(ctx, TradeDirection.None, "NO_LOGIC_BIAS", 0);
        }
        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            dynamic tuning,
            double hi,
            double lo,
            bool hasValidRange,
            double rangeAtr,
            Bar bar,
            int index)
        {
            int score = (int)tuning.BaseScore;
            int minScore = EntryDecisionPolicy.MinScoreThreshold;
            int setupScore = 0;
            double triggerScore = 0;

            var reasons = new List<string>();

            if (rangeAtr > 0)
            {
                if (rangeAtr <= 0.8)
                {
                    score += 6;
                    reasons.Add("FLAG_TIGHT");
                }
                else if (rangeAtr <= 1.1)
                {
                    score += 3;
                    reasons.Add("FLAG_OK");
                }
                else
                {
                    score -= 6;
                    reasons.Add("FLAG_LOOSE");
                }
            }
            else
            {
                reasons.Add("FLAG_RANGE_UNKNOWN");
                score -= 1;
            }

            bool hasImpulse =
                dir == TradeDirection.Long
                    ? ctx.HasImpulseLong_M5
                    : ctx.HasImpulseShort_M5;

            if (!hasImpulse)
                return InvalidDir(ctx, dir, "NO_IMPULSE", score);

            int barsSinceImpulse =
                dir == TradeDirection.Long
                    ? ctx.BarsSinceImpulseLong_M5
                    : ctx.BarsSinceImpulseShort_M5;

            if (barsSinceImpulse > tuning.MaxBarsSinceImpulse)
            {
                score -= 6;
                reasons.Add("IMPULSE_STALE");
            }

            if (barsSinceImpulse <= 1)
            {
                score += 6;
                reasons.Add("IMPULSE_FRESH");
            }
            else if (barsSinceImpulse <= 3)
            {
                score += 2;
                reasons.Add("IMPULSE_OK");
            }
            else
            {
                score -= 3;
                reasons.Add("IMPULSE_OLD");
            }

            bool hasFlag =
                dir == TradeDirection.Long
                    ? ctx.HasFlagLong_M5
                    : ctx.HasFlagShort_M5;

            bool earlyPB = ctx.HasEarlyPullback_M5;

            bool structuredPB =
                ctx.PullbackBars_M5 >= 2 &&
                ctx.IsPullbackDecelerating_M5;

            if (!(ctx.HasFlagLong_M5 || ctx.HasFlagShort_M5))
                return InvalidDir(ctx, dir, "NO_FLAG", score);

            score += 5;

            if (ctx.PullbackBars_M5 > 0)
                score += 4;

            if (ctx.HasEarlyPullback_M5)
                score += 1;

            bool hasSomeStructure =
                hasFlag || structuredPB || earlyPB;

            bool hasStructure =
                hasFlag
                || structuredPB
                || earlyPB;

            if (!hasStructure)
                setupScore -= 40;
            else
                setupScore += 20;

            if (!hasSomeStructure)
                reasons.Add("NO_STRUCTURE");

            ctx.Log?.Invoke($"[XAU FLAG] flag={hasFlag} earlyPB={earlyPB} structPB={structuredPB} score={score}");

            bool breakoutConfirmed =
                dir == TradeDirection.Long
                    ? ctx.FlagBreakoutUpConfirmed
                    : ctx.FlagBreakoutDownConfirmed;

            bool breakoutInstant =
                dir == TradeDirection.Long
                    ? ctx.FlagBreakoutUp
                    : ctx.FlagBreakoutDown;

            double breakDist = 0;

            if (hasValidRange)
            {
                breakDist =
                    dir == TradeDirection.Long
                        ? Math.Max(0, bar.Close - hi)
                        : Math.Max(0, lo - bar.Close);
            }

            double breakAtr = ctx.AtrM5 > 0 ? breakDist / ctx.AtrM5 : 0;

            double body = Math.Abs(bar.Close - bar.Open);
            double range = bar.High - bar.Low;
            double bodyRatio = range > 0 ? body / range : 0;

            bool strongBody = bodyRatio >= MinBodyRatio;

            bool earlyBreakout =
                breakoutInstant &&
                (!hasValidRange || breakAtr >= 0.03) &&
                bodyRatio >= 0.45 &&
                ctx.LastClosedBarInTrendDirection;

            bool hasConfirmation =
                breakoutConfirmed
                || earlyBreakout;

            if (hasConfirmation)
                setupScore += 20;

            if (breakoutInstant && !breakoutConfirmed)
            {
                score += 2;
                reasons.Add("BREAKOUT_FORMING");
            }

            bool confirmedBreakout =
                breakoutConfirmed &&
                strongBody &&
                (!hasValidRange || breakAtr >= MinCloseBreakAtr);

            if (confirmedBreakout)
            {
                score += 10;
                reasons.Add("BREAKOUT_CONFIRMED");
            }
            else if (earlyBreakout)
            {
                score += 6;
                reasons.Add("BREAKOUT_EARLY");
                minScore -= 2;
            }
            else
            {
                score -= (bodyRatio < 0.3) ? 6 : 3;
                reasons.Add("NO_BREAKOUT");
            }

            bool isAgainst =
                ctx.MetalHtfAllowedDirection != TradeDirection.None &&
                ctx.MetalHtfAllowedDirection != dir;

            if (isAgainst)
            {
                score -= HtfAgainstPenalty;
                reasons.Add("HTF_AGAINST");

                if (hasValidRange && breakAtr < HtfAgainstMinBreakAtr)
                {
                    score -= 3;
                    reasons.Add("HTF_WEAK_BREAK");
                }

                if (bodyRatio < HtfAgainstMinBody)
                {
                    score -= 3;
                    reasons.Add("HTF_WEAK_BODY");
                }

                if (!breakoutConfirmed && !earlyBreakout)
                {
                    score -= 6;
                    reasons.Add("HTF_NO_CONFIRM");
                }
            }

            if (dir == TradeDirection.Long && IsLowerHigh(ctx.M5, index))
                return InvalidDir(ctx, dir, "LOWER_HIGH", score);

            if (dir == TradeDirection.Short && IsHigherLow(ctx.M5, index))
                return InvalidDir(ctx, dir, "HIGHER_LOW", score);

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);
            score += setupScore;

            bool breakoutDetected = breakoutConfirmed || earlyBreakout;
            bool strongCandle = strongBody;
            bool followThrough = confirmedBreakout;

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

            ctx.Log?.Invoke(
                $"[TRIGGER SCORE] breakout={(breakoutDetected ? 1 : 0)} strong={(strongCandle ? 1 : 0)} follow={(followThrough ? 1 : 0)} total={triggerScore:F0} finalScore={score}");

            if (setupScore <= 0)
                score = Math.Min(score, minScore - 10);

            int effectiveMinScore = earlyBreakout ? minScore - 2 : minScore;

            bool valid = score >= effectiveMinScore;

            if (!valid)
                return InvalidDir(ctx, dir, "LOW_SCORE", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"ACCEPT {dir} score={score} breakoutConfirmed={breakoutConfirmed} earlyBreakout={earlyBreakout} breakAtr={breakAtr:F2} bodyRatio={bodyRatio:F2} rangeAtr={rangeAtr:F2}"
            };
        }

        private static bool IsLowerHigh(Bars b, int i)
        {
            if (i < 3) return false;
            return b[i - 1].High < b[i - 2].High && b[i - 2].High < b[i - 3].High;
        }

        private static bool IsHigherLow(Bars b, int i)
        {
            if (i < 3) return false;
            return b[i - 1].Low > b[i - 2].Low && b[i - 2].Low > b[i - 3].Low;
        }

        private static bool TryResolveSession(FxSession raw, out FxSession resolved, out string tag)
        {
            resolved = raw;
            tag = raw switch
            {
                FxSession.Asia => "XAU_FLAG_ASIA",
                FxSession.London => "XAU_FLAG_LONDON",
                FxSession.NewYork => "XAU_FLAG_NEWYORK",
                _ => null
            };
            return tag != null;
        }

        private static EntryEvaluation Reject(
            EntryContext ctx,
            string tag,
            FxSession session,
            dynamic tuning,
            EntryEvaluation buy,
            EntryEvaluation sell)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                Score = (int)tuning.BaseScore,
                IsValid = false,
                Reason = $"REJECT BOTH buy={buy.Score}/{buy.IsValid} sell={sell.Score}/{sell.IsValid}"
            };
        }

        private static EntryEvaluation InvalidDir(EntryContext ctx, TradeDirection dir, string reason, int score)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = reason
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                IsValid = false,
                Reason = reason
            };
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "XAU_FlagEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
