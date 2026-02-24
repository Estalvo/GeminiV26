using System;
using System.Collections.Generic;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes.METAL
{
    public class XAU_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Pullback;

        // === METAL softening knobs ===
        private const int FreshImpulsePenalty = 8;
        private const int AtrSpikePenalty = 8;
        private const int DeepPullbackPenalty = 10;
        private const int NoM1Penalty = 6;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, "CTX_NOT_READY");

            var reasons = new List<string>(8);

            // =========================
            // DIRECTION (TREND ONLY)
            // =========================
            TradeDirection dir = ctx.TrendDirection;
            if (dir != TradeDirection.Long && dir != TradeDirection.Short)
                return Reject(ctx, "NO_TREND_DIR");

            int baseScore = 60;
            int score = baseScore;
            reasons.Add($"Base={baseScore}");

            // =========================
            // HARD MARKET STATE GATES (XAU)
            // =========================
            if (ctx.MarketState == null || !ctx.MarketState.IsTrend)
                return Reject(ctx, "XAU_NO_TREND_STATE");

            if (ctx.MarketState.Adx < 16.0)
            {
                if (!ctx.HasImpulse_M5)
                {
                    return Reject(ctx, "ADX_LOW_NO_IMPULSE");
                }

                score -= 12;
                reasons.Add("ADX_LOW_WITH_IMPULSE(-12)");
            }

            // =========================
            // TIME MEMORY (XAU)
            // =========================

            // túl friss impulzus → SOFT
            if (ctx.BarsSinceImpulse_M5 == 0)
            {
                score -= FreshImpulsePenalty;
                reasons.Add($"FRESH_IMPULSE(-{FreshImpulsePenalty})");
            }

            // túl régi impulzus → HARD
            if (ctx.BarsSinceImpulse_M5 > 6)
                return RejectDecision(ctx, score, "STALE_IMPULSE", reasons);

            // pullback ne húzódjon el
            if (ctx.PullbackBars_M5 > 3)
                return RejectDecision(ctx, score, "PULLBACK_TOO_LONG", reasons);

            // =========================
            // VOLATILITY SPIKE FILTER
            // =========================
            if (ctx.BarsSinceImpulse_M5 <= 1 && ctx.IsAtrExpanding_M5)
            {
                score -= AtrSpikePenalty;
                reasons.Add($"ATR_SPIKE(-{AtrSpikePenalty})");
            }

            // =========================
            // IMPULSE REQUIREMENT
            // =========================
            if (ctx.HasImpulse_M5 || ctx.IsAtrExpanding_M5)
            {
                score += 10;
                reasons.Add("+IMPULSE(10)");
            }
            else
            {
                score -= 10;
                reasons.Add("WEAK_IMPULSE(-10)");
            }

            // =========================
            // PULLBACK QUALITY
            // =========================
            if (ctx.PullbackDepthAtr_M5 > 1.8)
            {
                bool htfAligned =
                    ctx.MetalHtfAllowedDirection == TradeDirection.None ||
                    ctx.MetalHtfAllowedDirection == dir;

                if (htfAligned)
                {
                    score -= DeepPullbackPenalty;
                    reasons.Add($"DEEP_PULLBACK_SOFT(-{DeepPullbackPenalty}) dATR={ctx.PullbackDepthAtr_M5:F2}");
                }
                else
                {
                    return RejectDecision(ctx, score, "PULLBACK_TOO_DEEP", reasons);
                }
            }
            else
            {
                score += 10;
                reasons.Add("+PB_OK(10)");
            }

            // =========================
            // M1 TRIGGER
            // =========================
            if (ctx.M1TriggerInTrendDirection)
            {
                score += 10;
                reasons.Add("+M1(10)");
            }
            else
            {
                score -= NoM1Penalty;
                reasons.Add($"NO_M1(-{NoM1Penalty})");
            }

            // Router floor kompatibilitás
            if (score > 0 && score < 20)
            {
                reasons.Add($"FLOOR_TO_20(from {score})");
                score = 20;
            }

            // =========================
            // DYNAMIC MIN SCORE (XAU)
            // =========================
            int minScore = 68;

            if (ctx.PullbackDepthAtr_M5 > 1.2)
                minScore += 4;

            if (ctx.PullbackDepthAtr_M5 > 1.8)
                minScore += 4;

            if (ctx.BarsSinceImpulse_M5 >= 4)
                minScore += 4;

            // FINAL CHECK
            if (score < minScore)
                return RejectDecision(ctx, score, $"LOW_SCORE({score})", reasons, minScore);
                
            // =========================
            // ACCEPT
            // =========================
            string note =
                $"[XAU_PB] {ctx.Symbol} dir={dir} " +
                $"Score={score} Min={minScore} Decision=ACCEPT | " +
                string.Join(" | ", reasons);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                //LogicConfidence = ctx.LogicConfidence,  
                IsValid = true,
                Reason = note
            };
        }

        // =====================================================
        // REJECT HELPERS
        // =====================================================
        private EntryEvaluation Reject(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                IsValid = false,
                Reason = reason
            };
        }

        private EntryEvaluation RejectDecision(
            EntryContext ctx,
            int score,
            string reason,
            List<string> reasons,
            int? minScore = null)
        {
            string note =
                $"[XAU_PB] {ctx?.Symbol} dir={ctx?.TrendDirection} " +
                $"Score={score}" +
                (minScore.HasValue ? $" Min={minScore.Value}" : "") +
                $" Decision=REJECT Reason={reason} | " +
                (reasons != null ? string.Join(" | ", reasons) : "");

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = ctx?.TrendDirection ?? TradeDirection.None,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = note
            };
        }
    }
}
