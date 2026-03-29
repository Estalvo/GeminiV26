using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Core;
using GeminiV26.Core.Matrix;

namespace GeminiV26.EntryTypes.METAL
{
    /// <summary>
    /// XAU Impulse Continuation Entry – LIVE VERSION
    /// Phase 3.9 – Momentum continuation for metals
    ///
    /// Belép, ha:
    /// - M5 ATR expanzió
    /// - M5 impulse
    /// - ADX minimum szint
    /// - EMA alignment irányba
    /// - M1 trigger megerősítés
    /// </summary>
    public class XAU_ImpulseEntry : IEntryType
    {
        public EntryType Type => EntryType.XAU_Impulse;

        private const double SlopeEps = 0.0;
        private const int MinScore = EntryDecisionPolicy.MinScoreThreshold;
        private const double MinAdx = 18.0;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowBreakout)
                return Reject(ctx, "SESSION_MATRIX_BREAKOUT_DISABLED");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            if (ctx.LogicBias == TradeDirection.None)
                return new EntryEvaluation
                {
                    Symbol = ctx?.Symbol,
                    Type = Type,
                    Direction = TradeDirection.None,
                    Score = 0,
                    IsValid = false,
                    Reason = "NO_LOGIC_BIAS"
                };

            bool htfMismatch =
                ctx.ResolveAssetHtfConfidence01() >= 0.6 &&
                ctx.ResolveAssetHtfAllowedDirection() != TradeDirection.None &&
                ctx.ResolveAssetHtfAllowedDirection() != ctx.LogicBias;
            if (htfMismatch)
                ctx.Log?.Invoke($"[HTF][SOFT_MISMATCH] entryType={Type} dir={ctx.LogicBias} htf={ctx.ResolveAssetHtfAllowedDirection()} conf={ctx.ResolveAssetHtfConfidence01():0.00}");

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateSide(ctx, matrix, TradeDirection.Long, htfMismatch);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateSide(ctx, matrix, TradeDirection.Short, htfMismatch);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                IsValid = false,
                Reason = "NO_LOGIC_BIAS"
            };
        }
        private EntryEvaluation EvaluateSide(EntryContext ctx, SessionMatrixConfig matrix, TradeDirection dir, bool htfMismatch)
        {
            int score = 60;
            int setupScore = 0;

            if (htfMismatch)
            {
                score -= 8;
                ctx.Log?.Invoke($"[HTF][SCORE_PENALTY] entryType={Type} dir={dir} penalty=8 score={score}");
            }

            // =====================================================
            // 1️⃣ DIRECTIONAL CONTEXT
            // =====================================================
            TradeDirection slopeDirection = ResolveXauDirection(ctx);
            if (slopeDirection == dir)
                score += 8;
            else if (slopeDirection != TradeDirection.None)
                score -= 8;

            if (!ctx.IsAtrExpanding_M5)
                score -= 6;

            score += 5;

            if (!ctx.HasImpulse_M5)
            {
                if (ctx.TrendDirection == dir)
                    score -= 8;
                else
                    return Reject(ctx, $"NO_M5_IMPULSE_{dir}");
            }

            score += 5;

            double minAdxRequired = 18.0;

            // XAU impulse continuationhez erősebb trend kell
            if (SymbolRouting.ResolveInstrumentClass(ctx.Symbol) == InstrumentClass.METAL)
                minAdxRequired = 28.0;

            minAdxRequired = System.Math.Max(minAdxRequired, matrix.MinAdx);

            if (ctx.Adx_M5 >= 15 && ctx.Adx_M5 < minAdxRequired)
            {
                if (ctx.Adx_M5 < 28 && ctx.TrendDirection == dir && ctx.MarketState?.IsTrend == true)
                    score -= 8;
                else
                    return Reject(ctx, $"ADX_TOO_LOW_{dir}({ctx.Adx_M5:F1})");
            }

            if (ctx.Adx_M5 >= 30)
                score += 5;

            // =====================================================
            // 5️⃣ EMA ALIGNMENT
            // =====================================================
            if (dir == TradeDirection.Long)
            {
                if (ctx.Ema8_M5 > ctx.Ema21_M5)
                    score += 5;
            }
            else
            {
                if (ctx.Ema8_M5 < ctx.Ema21_M5)
                    score += 5;
            }

            // =====================================================
            // 6️⃣ M1 TRIGGER (Continuation confirmation)
            // =====================================================
            if (!ctx.M1TriggerInTrendDirection)
                score -= 8;

            score += 5;

            bool hasFlag =
                dir == TradeDirection.Long
                    ? ctx.HasFlagLong_M5
                    : ctx.HasFlagShort_M5;

            bool structuredPB =
                ctx.IsPullbackDecelerating_M5 &&
                ctx.PullbackBars_M5 >= 2;

            bool earlyPB =
                ctx.HasEarlyPullback_M5;

            bool hasStructure =
                hasFlag
                || structuredPB
                || earlyPB;

            if (!hasStructure)
                setupScore -= 40;
            else
                setupScore += 20;

            bool breakoutConfirmed =
                ctx.HasBreakout_M1 &&
                ctx.BreakoutDirection == dir;

            bool earlyBreakout =
                ctx.M1TriggerInTrendDirection;

            bool hasConfirmation =
                breakoutConfirmed
                || earlyBreakout;

            if (hasConfirmation)
                setupScore += 20;

            int lastClosed = ctx.M5.Count - 2;
            var bar = ctx.M5[lastClosed];
            bool breakoutDetected = breakoutConfirmed || earlyBreakout;
            bool strongCandle =
                (dir == TradeDirection.Long && bar.Close > bar.Open) ||
                (dir == TradeDirection.Short && bar.Close < bar.Open);
            bool followThrough = hasConfirmation;

            int scoreBeforeTriggerModel = score;
            score = TriggerScoreModel.Apply(ctx, $"XAU_IMPULSE_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_IMPULSE_TRIGGER");
            bool earlyWeakness = score < scoreBeforeTriggerModel;


            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, true);

            // =====================================================
            // FINAL DECISION
            // =====================================================
            score += (int)System.Math.Round(matrix.EntryScoreModifier);
            score += setupScore;

            if (setupScore <= 0)
                score = System.Math.Min(score, MinScore - 10);

            bool noMomentum = !(ctx.MarketState?.IsMomentum ?? false);
            if (ctx.Symbol == "XAUUSD"
                && ctx.MarketState != null
                && ctx.MarketState.IsTrend
                && noMomentum
                && earlyWeakness)
            {
                int scoreBeforeCompression = score;
                score = (int)System.Math.Round(score * 0.65);
                ctx.Log?.Invoke(
                    $"[XAU CONTINUATION FILTER] score {scoreBeforeCompression}->{score} momentum={ctx.MarketState.IsMomentum} earlyWeakness={earlyWeakness}");
            }


            string note =
                $"[XAU_IMPULSE_CONT] {ctx.Symbol} dir={dir} " +
                $"Score={score} ADX={ctx.Adx_M5:F1} ATR_EXP=True IMPULSE=True";

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                Score = score,
                IsValid = score >= MinScore,
                Reason = note
            };
        }

        // =====================================================
        // IRÁNY MEGHATÁROZÁS – változatlan
        // =====================================================
        private TradeDirection ResolveXauDirection(EntryContext ctx)
        {
            bool up5 = ctx.Ema21Slope_M5 > SlopeEps;
            bool dn5 = ctx.Ema21Slope_M5 < -SlopeEps;

            bool up15 = ctx.Ema21Slope_M15 > SlopeEps;
            bool dn15 = ctx.Ema21Slope_M15 < -SlopeEps;

            if (up5 && up15) return TradeDirection.Long;
            if (dn5 && dn15) return TradeDirection.Short;

            double a5 = System.Math.Abs(ctx.Ema21Slope_M5);
            double a15 = System.Math.Abs(ctx.Ema21Slope_M15);

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
                IsValid = false,
                Reason = $"[XAU_IMPULSE_REJECT] {reason}"
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
                    TypeTag = "XAU_ImpulseEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

    }
}
