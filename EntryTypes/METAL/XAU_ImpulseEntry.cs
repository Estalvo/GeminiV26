using System;
using System.Collections.Generic;
using GeminiV26.Core.Entry;
using GeminiV26.Core;
using GeminiV26.Core.Matrix;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Impulse;

        private const double SlopeEps = 0.0;
        private const int MinScoreThreshold = 65;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowBreakout)
                return Reject(ctx, "SESSION_MATRIX_BREAKOUT_DISABLED");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            TradeDirection dir = ResolveXauDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, "NO_TREND_CONTEXT");

            int score = 60;
            int setupScore = 0;
            var reasons = new List<string>();

            if (XauEntryDecisionPolicy.IsTrendSafetyBlock(ctx, dir, out string hardReason))
                return Build(ctx, dir, score, false, hardReason, reasons);

            if (!ctx.IsAtrExpanding_M5)
            {
                score -= 10;
                reasons.Add("ATR_NOT_EXPANDING(-10)");
            }
            else
            {
                score += 5;
                reasons.Add("ATR_EXPANDING(+5)");
            }

            if (!ctx.HasImpulse_M5)
            {
                score -= 14;
                reasons.Add("NO_M5_IMPULSE(-14)");
            }
            else
            {
                score += 5;
                reasons.Add("M5_IMPULSE(+5)");
            }

            double minAdxRequired = SymbolRouting.ResolveInstrumentClass(ctx.Symbol) == InstrumentClass.METAL
                ? 28.0
                : 18.0;
            minAdxRequired = Math.Max(minAdxRequired, matrix.MinAdx);

            if (ctx.Adx_M5 < minAdxRequired)
            {
                score -= 10;
                reasons.Add($"ADX_TOO_LOW(-10:{ctx.Adx_M5:F1}<{minAdxRequired:F1})");
            }
            else if (ctx.Adx_M5 >= 30)
            {
                score += 5;
                reasons.Add("ADX_STRONG(+5)");
            }

            if (dir == TradeDirection.Long)
            {
                if (ctx.Ema8_M5 > ctx.Ema21_M5)
                {
                    score += 5;
                    reasons.Add("EMA_ALIGN(+5)");
                }
                else
                {
                    score -= 5;
                    reasons.Add("EMA_MISALIGN(-5)");
                }
            }
            else
            {
                if (ctx.Ema8_M5 < ctx.Ema21_M5)
                {
                    score += 5;
                    reasons.Add("EMA_ALIGN(+5)");
                }
                else
                {
                    score -= 5;
                    reasons.Add("EMA_MISALIGN(-5)");
                }
            }

            if (!ctx.M1TriggerInTrendDirection)
            {
                score -= 8;
                reasons.Add("NO_M1_TRIGGER(-8)");
            }
            else
            {
                score += 5;
                reasons.Add("M1_TRIGGER_OK(+5)");
            }

            bool hasFlag = dir == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5;
            bool structuredPB = ctx.IsPullbackDecelerating_M5 && ctx.PullbackBars_M5 >= 2;
            bool earlyPB = ctx.HasEarlyPullback_M5;
            bool hasStructure = hasFlag || structuredPB || earlyPB;

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

            bool breakoutConfirmed = ctx.HasBreakout_M1 && ctx.BreakoutDirection == dir;
            bool earlyBreakout = ctx.M1TriggerInTrendDirection;
            if (breakoutConfirmed || earlyBreakout)
            {
                setupScore += 20;
                reasons.Add("CONFIRMATION_OK(+20)");
            }
            else
            {
                setupScore -= 10;
                reasons.Add("PARTIAL_CONFIRMATION(-10)");
            }

            if (ctx.IsTransition_M5)
            {
                score -= 6;
                reasons.Add("TRANSITION_PENALTY(-6)");
            }

            if (ctx.MetalHtfAllowedDirection != TradeDirection.None && ctx.MetalHtfAllowedDirection != dir)
            {
                score -= 5;
                reasons.Add("HTF_AGAINST(-5)");
            }

            score += (int)Math.Round(matrix.EntryScoreModifier);
            score += setupScore;
            XauEntryDecisionPolicy.ApplyLogicBiasScore(ctx, dir, ref score, reasons);

            return Build(ctx, dir, score, true, "SCORE_DRIVEN", reasons);
        }

        private TradeDirection ResolveXauDirection(EntryContext ctx)
        {
            bool up5 = ctx.Ema21Slope_M5 > SlopeEps;
            bool dn5 = ctx.Ema21Slope_M5 < -SlopeEps;
            bool up15 = ctx.Ema21Slope_M15 > SlopeEps;
            bool dn15 = ctx.Ema21Slope_M15 < -SlopeEps;

            if (up5 && up15) return TradeDirection.Long;
            if (dn5 && dn15) return TradeDirection.Short;

            double a5 = Math.Abs(ctx.Ema21Slope_M5);
            double a15 = Math.Abs(ctx.Ema21Slope_M15);
            if (a5 <= 0 && a15 <= 0) return TradeDirection.None;

            if (a15 >= a5 * 0.9)
            {
                if (up15) return TradeDirection.Long;
                if (dn15) return TradeDirection.Short;
            }

            if (up5) return TradeDirection.Long;
            if (dn5) return TradeDirection.Short;
            return TradeDirection.None;
        }

        private EntryEvaluation Reject(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                MinScoreThreshold = MinScoreThreshold,
                IsValid = false,
                Reason = $"[XAU_IMPULSE][ENTRY DECISION] score=0 threshold={MinScoreThreshold} valid=false state={reason}"
            };
        }

        private EntryEvaluation Build(EntryContext ctx, TradeDirection dir, int score, bool isValid, string state, List<string> reasons)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = Math.Max(0, score),
                MinScoreThreshold = MinScoreThreshold,
                IsValid = isValid,
                LogicConfidence = ctx?.LogicBiasConfidence ?? 0,
                Reason = $"[XAU_IMPULSE][ENTRY DECISION] score={Math.Max(0, score)} threshold={MinScoreThreshold} valid={isValid} state={state} dir={dir} :: {string.Join(" | ", reasons ?? new List<string>())}"
            };
        }
    }
}
