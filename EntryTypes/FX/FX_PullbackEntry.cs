using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    /// <summary>
    /// FX Pullback entry – tightened validity gates + FlagEntry-style regulation.
    /// Goal: stop "forcing" low-quality losers by hard-blocking weak market states,
    /// while still letting the router rank between valid candidates.
    /// </summary>
    public class FX_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_Pullback;

        // Keep your existing baseline. If you want fewer trades, raise this (e.g. 40–45).
        private const int MIN_SCORE = 35;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 60;   // baseline
            int penalty = 0;  // accumulated penalty (budgeted)

            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", score);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Block(ctx, "NO_FX_PROFILE", score);

            // FlagEntry-style penalty budget (prevents "death by a thousand cuts")
            int penaltyBudget = Math.Max(0, fx.EntryPenaltyBudget);

            // =========================
            // TREND RESOLUTION (HARD)
            // =========================
            bool trendUp = ctx.Ema21Slope_M15 > 0 && ctx.Ema21Slope_M5 > 0;
            bool trendDown = ctx.Ema21Slope_M15 < 0 && ctx.Ema21Slope_M5 < 0;

            TradeDirection dir =
                trendUp ? TradeDirection.Long :
                trendDown ? TradeDirection.Short :
                TradeDirection.None;

            // Pullback without a clean trend is where it "forces losers" most often.
            if (dir == TradeDirection.None)
                return Block(ctx, "NO_TREND_DIR", score);

            // =========================
            // ADX HARD / SOFT (copied style from FlagEntry)
            // =========================
            // Asia is noisier -> allow slightly lower ADX, but still hard-gate.
            double dynamicMinAdx = (ctx.Session == FxSession.Asia) ? 18.0 : 20.0;

            if (ctx.Adx_M5 < dynamicMinAdx)
                return Block(ctx, $"ADX_TOO_LOW_{ctx.Adx_M5:0.0}", score);

            if (ctx.Adx_M5 < 23.0)
            {
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "ADX_SOFT_LOW");
            }
            else if (ctx.Adx_M5 >= 40.0 && ctx.AdxSlope_M5 <= 0)
            {
                ApplyPenalty(ref score, ref penalty, 5, penaltyBudget, ctx, "ADX_EXHAUST_SOFT");
            }

            // =========================
            // IMPULSE QUALITY (HARD-ish)
            // =========================
            // No impulse -> most pullbacks are just chop. FlagEntry is strict; we mimic that.
            if (!ctx.HasImpulse_M5)
            {
                // Asia: hard block without impulse (your old logic already penalized heavily)
                if (ctx.Session == FxSession.Asia)
                    return Block(ctx, "ASIA_NO_IMPULSE", score);

                ApplyPenalty(ref score, ref penalty, 8, penaltyBudget, ctx, "NO_IMPULSE");
            }
            else
            {
                if (ctx.BarsSinceImpulse_M5 <= 2)
                    score += 6;
                else if (ctx.BarsSinceImpulse_M5 <= 5)
                    score += 2;
                else
                    ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "IMPULSE_TOO_OLD");
            }

            // =========================
            // PULLBACK QUALITY (mostly HARD)
            // =========================
            if (!ctx.PullbackTouchedEma21_M5)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_NO_EMA21_TOUCH");

            if (!ctx.IsPullbackDecelerating_M5)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_NOT_DECEL");

            if (!ctx.HasReactionCandle_M5)
                ApplyPenalty(ref score, ref penalty, 4, penaltyBudget, ctx, "PB_NO_REACTION");

            if (!ctx.LastClosedBarInTrendDirection)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_LASTBAR_NOT_TREND_DIR");

            // Depth gates (keep your original hard extreme)
            if (ctx.PullbackDepthAtr_M5 > 1.6)
                return Block(ctx, "PB_TOO_DEEP_EXTREME", score);

            if (ctx.PullbackDepthAtr_M5 > 1.0)
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "PB_TOO_DEEP");

            // =========================
            // ENERGY MODEL (keep, but budgeted)
            // =========================
            int fuel = 0;

            if (ctx.Adx_M5 >= 25) fuel += 4;
            else fuel -= 6;

            if (ctx.AdxSlope_M5 > 0) fuel += 4;
            else fuel -= 4;

            if (ctx.IsAtrExpanding_M5) fuel += 3;
            else fuel -= 3;

            if (ctx.BarsSinceImpulse_M5 <= 2) fuel += 4;

            score += fuel;

            // Hard exhaustion (same as your original)
            if (ctx.Adx_M5 > 45 &&
                ctx.AdxSlope_M5 <= 0 &&
                !ctx.IsAtrExpanding_M5)
            {
                return Block(ctx, "TREND_EXHAUSTION", score);
            }

            // =========================
            // SESSION MODULATION
            // =========================
            if (ctx.Session == FxSession.Asia)
            {
                ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "ASIA_SOFT");
            }

            if (ctx.Session == FxSession.NewYork)
            {
                if (!ctx.M1TriggerInTrendDirection)
                    ApplyPenalty(ref score, ref penalty, 6, penaltyBudget, ctx, "NY_NO_M1_TRIGGER");
            }

            // =========================
            // FLAG PRIORITY (avoid collision)
            // =========================
            if (ctx.IsValidFlagStructure_M5)
                ApplyPenalty(ref score, ref penalty, fx.PbLondonFlagPriorityPenalty, penaltyBudget, ctx, "FLAG_PRIORITY_PEN");

            // =========================
            // HTF SOFT
            // =========================
            if (ctx.FxHtfAllowedDirection != TradeDirection.None &&
                ctx.FxHtfAllowedDirection != dir)
            {
                double conf = ctx.FxHtfConfidence01;
                int htfPenalty = (int)(conf * 10);

                ApplyPenalty(ref score, ref penalty, htfPenalty, penaltyBudget, ctx, "HTF_MISMATCH");

                // If HTF is strong and local fuel weak -> block (same intent as original)
                if (conf >= 0.75 && fuel < 3)
                    return Block(ctx, "HTF_DOMINANT_BLOCK", score);
            }

            // =========================
            // PENALTY BUDGET GUARD
            // =========================
            if (penaltyBudget > 0 && penalty > penaltyBudget)
                return Block(ctx, $"PENALTY_BUDGET_EXCEEDED_{penalty}/{penaltyBudget}", score);

            // =========================
            // FINAL SCORE CHECK
            // =========================
            if (score < MIN_SCORE)
                return Block(ctx, $"LOW_SCORE_{score}", score);

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason = $"FX_PULLBACK_V3 dir={dir} score={score} pen={penalty}/{penaltyBudget}"
            };
        }

        private static void ApplyPenalty(
            ref int score,
            ref int penalty,
            int amount,
            int budget,
            EntryContext ctx,
            string tag)
        {
            if (amount <= 0) return;

            penalty += amount;
            score -= amount;

            // FlagEntry-style debug line (no Console; keep it safe for cTrader compile)
            ctx?.Log?.Invoke($"[FX_PullbackEntry] PEN {tag} -{amount} | score={score} pen={penalty}/{budget}");
        }

        private EntryEvaluation Block(EntryContext ctx, string reason, int score)
        {
            ctx?.Log?.Invoke($"[FX_PullbackEntry] BLOCK {reason} | score={score}");
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_Pullback,
                Direction = TradeDirection.None,
                Score = score,
                IsValid = false,
                Reason = reason
            };
        }
    }
}
