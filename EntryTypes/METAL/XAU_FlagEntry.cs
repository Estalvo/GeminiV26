using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
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

            if (double.IsNaN(hi) || double.IsNaN(lo) || double.IsInfinity(hi) || double.IsInfinity(lo))
                return Invalid(ctx, "DATA_INTEGRITY", 0);
                
            double rangeAtr = hasValidRange && ctx.AtrM5 > 0
                ? (hi - lo) / ctx.AtrM5
                : 0;

            if (hasValidRange && rangeAtr > tuning.MaxFlagAtrMult * 1.3)
                ctx.Log?.Invoke($"[FLAG WARN] Wide range kept as soft signal rangeAtr={rangeAtr:F2} maxHard={tuning.MaxFlagAtrMult * 1.3:F2}");

            bool ambiguousSpike = false;
            if (hasValidRange)
            {
                bool wickBoth = bar.High >= hi && bar.Low <= lo;
                bool closeUp = bar.Close > hi;
                bool closeDn = bar.Close < lo;
                ambiguousSpike = wickBoth && !closeUp && !closeDn;
            }

            var buy = EvaluateSide(TradeDirection.Long, ctx, tuning, hi, lo, hasValidRange, rangeAtr, ambiguousSpike, bar, last);
            var sell = EvaluateSide(TradeDirection.Short, ctx, tuning, hi, lo, hasValidRange, rangeAtr, ambiguousSpike, bar, last);

            if (buy.IsValid && !sell.IsValid) return buy;
            if (!buy.IsValid && sell.IsValid) return sell;

            int diff = buy.Score - sell.Score;

            if (Math.Abs(diff) <= ScoreDeadband)
            {
                if (ctx.BreakoutUpBarsSince < ctx.BreakoutDownBarsSince)
                    return buy;

                if (ctx.BreakoutDownBarsSince < ctx.BreakoutUpBarsSince)
                    return sell;
            }

            return diff >= 0 ? buy : sell;
        }

        private EntryEvaluation EvaluateSide(
            TradeDirection dir,
            EntryContext ctx,
            dynamic tuning,
            double hi,
            double lo,
            bool hasValidRange,
            double rangeAtr,
            bool ambiguousSpike,
            Bar bar,
            int index)
        {
            int score = (int)tuning.BaseScore;
            int minScore = (int)tuning.MinScore;
            int setupScore = 0;

            var reasons = new List<string>();

            if (XauEntryDecisionPolicy.IsTrendSafetyBlock(ctx, dir, out string hardReason))
                return InvalidDir(ctx, dir, hardReason, score, minScore);

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
            {
                score -= 18;
                reasons.Add("NO_IMPULSE(-18)");
            }

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
            {
                score -= 12;
                reasons.Add("NO_FLAG_RANGE(-12)");
            }
            else
            {
                score += 5;
            }

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
            {
                setupScore -= 20;
                reasons.Add("WEAK_STRUCTURE(-20)");
            }
            else
            {
                setupScore += 20;
                reasons.Add("STRUCTURE_OK(+20)");
            }

            if (!hasSomeStructure)
                reasons.Add("IMPERFECT_FLAG(-0)");

            if (!hasValidRange)
            {
                score -= 12;
                reasons.Add("NO_FLAG_RANGE(-12)");
            }

            if (ambiguousSpike)
            {
                score -= 10;
                reasons.Add("AMBIGUOUS_SPIKE(-10)");
            }

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
            else
            {
                setupScore -= 10;
                reasons.Add("PARTIAL_CONFIRMATION(-10)");
            }

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
            {
                score -= 8;
                reasons.Add("LOWER_HIGH(-8)");
            }

            if (dir == TradeDirection.Short && IsHigherLow(ctx.M5, index))
            {
                score -= 8;
                reasons.Add("HIGHER_LOW(-8)");
            }

            score += setupScore;
            XauEntryDecisionPolicy.ApplyLogicBiasScore(ctx, dir, ref score, reasons);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = Math.Max(0, score),
                MinScoreThreshold = earlyBreakout ? minScore - 2 : minScore,
                LogicConfidence = ctx.LogicBiasConfidence,
                IsValid = true,
                Reason = $"[XAU_FLAG][ENTRY DECISION] score={Math.Max(0, score)} threshold={(earlyBreakout ? minScore - 2 : minScore)} valid=true dir={dir} breakoutConfirmed={breakoutConfirmed} earlyBreakout={earlyBreakout} breakAtr={breakAtr:F2} bodyRatio={bodyRatio:F2} rangeAtr={rangeAtr:F2} :: {string.Join(" | ", reasons)}"
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
            return buy.Score >= sell.Score ? buy : sell;
        }

        private static EntryEvaluation InvalidDir(EntryContext ctx, TradeDirection dir, string reason, int score, int minScore)
        {
            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = dir,
                Score = Math.Max(0, score),
                MinScoreThreshold = minScore,
                IsValid = false,
                Reason = $"[XAU_FLAG][ENTRY DECISION] score={Math.Max(0, score)} threshold={minScore} valid=false dir={dir} state={reason}"
            };
        }

        private static EntryEvaluation Invalid(EntryContext ctx, string reason, int score = 0)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.XAU_Flag,
                Direction = TradeDirection.None,
                Score = Math.Max(0, score),
                MinScoreThreshold = 0,
                IsValid = false,
                Reason = $"[XAU_FLAG][ENTRY DECISION] score={Math.Max(0, score)} threshold=0 valid=false state={reason}"
            };
        }
    }
}
