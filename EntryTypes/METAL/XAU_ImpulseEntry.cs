using GeminiV26.Core.Entry;
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
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowBreakout)
                return Reject(ctx, "SESSION_MATRIX_BREAKOUT_DISABLED");

            if (ctx == null || !ctx.IsReady || ctx.M5 == null)
                return Reject(ctx, "CTX_NOT_READY");

            var longEval = EvaluateSide(ctx, matrix, TradeDirection.Long);
            var shortEval = EvaluateSide(ctx, matrix, TradeDirection.Short);

            return EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval);
        }

        private EntryEvaluation EvaluateSide(EntryContext ctx, SessionMatrixConfig matrix, TradeDirection dir)
        {
            int score = 60;
            int setupScore = 0;

            // =====================================================
            // 1️⃣ DIRECTIONAL CONTEXT
            // =====================================================
            TradeDirection slopeDirection = ResolveXauDirection(ctx);
            if (slopeDirection == dir)
                score += 8;
            else if (slopeDirection != TradeDirection.None)
                score -= 8;

            if (!ctx.IsAtrExpanding_M5)
                return Reject(ctx, $"ATR_NOT_EXPANDING_{dir}", dir, score);

            score += 5;

            if (!ctx.HasImpulse_M5)
                return Reject(ctx, $"NO_M5_IMPULSE_{dir}", dir, score);

            score += 5;

            double minAdxRequired = 18.0;

            // XAU impulse continuationhez erősebb trend kell
            if (SymbolRouting.ResolveInstrumentClass(ctx.Symbol) == InstrumentClass.METAL)
                minAdxRequired = 28.0;

            minAdxRequired = System.Math.Max(minAdxRequired, matrix.MinAdx);

            if (ctx.Adx_M5 < minAdxRequired)
                return Reject(ctx, $"ADX_TOO_LOW_{dir}({ctx.Adx_M5:F1})", dir, score);

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

            score = TriggerScoreModel.Apply(ctx, $"XAU_IMPULSE_{dir}", score, breakoutDetected, strongCandle, followThrough, "NO_IMPULSE_TRIGGER");

            // =====================================================
            // FINAL DECISION
            // =====================================================
            score += (int)System.Math.Round(matrix.EntryScoreModifier);
            score += setupScore;

            if (setupScore <= 0)
                score = System.Math.Min(score, MinScore - 10);


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
            => Reject(ctx, reason, TradeDirection.None, 0);

        private EntryEvaluation Reject(EntryContext ctx, string reason, TradeDirection dir, int score)
        {
            bool hardInvalid = EntryDecisionPolicy.IsHardInvalidReason(reason) || dir == TradeDirection.None;
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = hardInvalid ? TradeDirection.None : dir,
                Score = score,
                IsValid = false,
                Reason = $"[XAU_IMPULSE_REJECT] {reason}"
            };
        }
    }
}
