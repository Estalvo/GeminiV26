using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
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
        private const int ATR_REL_LOOKBACK = 20;
        private const double ATR_REL_EXPANSION_FACTOR = 0.85;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            int score = 60;   // baseline
            int penalty = 0;  // accumulated penalty (budgeted)

            if (ctx == null || !ctx.IsReady)
                return Block(ctx, "CTX_NOT_READY", score);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Block(ctx, "NO_FX_PROFILE", score);

            var matrix = ctx.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Block(ctx, "SESSION_MATRIX_PULLBACK_DISABLED", score);

            double atrAvg20 = ComputeAtrAverage(ctx, ATR_REL_LOOKBACK);
            if (atrAvg20 <= 0)
                return Block(ctx, "SESSION_MATRIX_ATR_AVG_UNAVAILABLE", score);

            double atrRelativeThreshold = atrAvg20 * ATR_REL_EXPANSION_FACTOR;
            bool atrRelativePass = ctx.AtrM5 >= atrRelativeThreshold;
            ctx?.Log?.Invoke($"[FX_PB ATR] atr={ctx.AtrM5:F2} avg20={atrAvg20:F2} thr={atrRelativeThreshold:F2} pass={atrRelativePass}");

            if (!atrRelativePass)
            {
                ctx?.Log?.Invoke($"[FX_PullbackEntry] BLOCK ATR_RELATIVE_TOO_LOW atr={ctx.AtrM5:F2} avg20={atrAvg20:F2} threshold={atrRelativeThreshold:F2}");
                return Block(ctx, "[ROUTER] SESSION_MATRIX_ATR_TOO_LOW_RELATIVE", score);
            }

            if (matrix.MinEmaDistance > 0 && System.Math.Abs(ctx.Ema8_M5 - ctx.Ema21_M5) < matrix.MinEmaDistance)
                return Block(ctx, "SESSION_MATRIX_EMA_DISTANCE_TOO_LOW", score);

            // FlagEntry-style penalty budget (prevents "death by a thousand cuts")
            int penaltyBudget = 10;   // vagy 8–12, amit a FlagEntry-ben használsz

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
            dynamicMinAdx = System.Math.Max(dynamicMinAdx, matrix.MinAdx);

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

            // HARD STRUCTURE CHECK (ne éljen túl 2 gyengeséget)
            int weakCount = 0;

            if (!ctx.PullbackTouchedEma21_M5) weakCount++;
            if (!ctx.IsPullbackDecelerating_M5) weakCount++;
            if (!ctx.HasReactionCandle_M5) weakCount++;
            if (!ctx.LastClosedBarInTrendDirection) weakCount++;

            if (weakCount >= 2)
                return Block(ctx, "PB_WEAK_STRUCTURE", score);
                
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
            // FLAG COLLISION (SOFT BUT STRONG)
            // =========================
            if (ctx.IsValidFlagStructure_M5)
            {
                int flagPenalty = ctx.M1TriggerInTrendDirection ? 6 : 10;

                ApplyPenalty(
                    ref score,
                    ref penalty,
                    flagPenalty,
                    penaltyBudget,
                    ctx,
                    ctx.M1TriggerInTrendDirection ? "FLAG_ACTIVE_WITH_M1" : "FLAG_ACTIVE_NO_M1"
                );
            }

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
            score += (int)System.Math.Round(matrix.EntryScoreModifier);

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

        private static double ComputeAtrAverage(EntryContext ctx, int lookback)
        {
            if (ctx?.M5 == null || lookback <= 0 || ctx.M5.Count <= lookback + 1)
                return 0;

            double trSum = 0;
            for (int i = 1; i <= lookback; i++)
            {
                double high = ctx.M5.HighPrices.Last(i);
                double low = ctx.M5.LowPrices.Last(i);
                double prevClose = ctx.M5.ClosePrices.Last(i + 1);

                double trHighLow = high - low;
                double trHighPrevClose = System.Math.Abs(high - prevClose);
                double trLowPrevClose = System.Math.Abs(low - prevClose);

                trSum += System.Math.Max(trHighLow, System.Math.Max(trHighPrevClose, trLowPrevClose));
            }

            return trSum / lookback;
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
