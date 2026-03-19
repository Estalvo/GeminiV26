using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public sealed class FX_MicroStructureEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_MicroStructure;

        private const double MinPullbackAtr = 0.15;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 30)
                return Invalid(ctx, TradeDirection.None, "CTX_NOT_READY", 0);

            if (ctx.AtrM5 <= 0)
                return Invalid(ctx, TradeDirection.None, "ATR_NOT_READY", 0);

            var fx = FxInstrumentMatrix.Get(ctx.Symbol);
            if (fx == null)
                return Invalid(ctx, TradeDirection.None, "NO_FX_PROFILE", 0);

            // evaluate both directions
            var longEval = EvalForDir(ctx, fx, TradeDirection.Long);
            var shortEval = EvalForDir(ctx, fx, TradeDirection.Short);

            if (longEval.IsValid && shortEval.IsValid)
                return EntryDecisionPolicy.Normalize(EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval));

            if (longEval.IsValid) return longEval;
            if (shortEval.IsValid) return shortEval;

            return EntryDecisionPolicy.Normalize(EntryDecisionPolicy.SelectBalancedEvaluation(ctx, Type, longEval, shortEval));
        }

        private static EntryEvaluation EvalForDir(
            EntryContext ctx,
            dynamic fx,
            TradeDirection dir)
        {
            int score = 54;   // base score aligned with FlagEntry universe
            int setupScore = 0;
            double triggerScore = 0;

            ctx.Log?.Invoke(
                $"[FX_MICRO START] sym={ctx.Symbol} dir={dir} " +
                $"htf={ctx.FxHtfAllowedDirection}/{ctx.FxHtfConfidence01:F2} " +
                $"impulse={ctx.HasImpulse_M5} atrExp={ctx.IsAtrExpanding_M5} range={ctx.IsRange_M5}"
            );

            int barsSinceBreak =
                dir == TradeDirection.Long
                ? ctx.BarsSinceHighBreak_M5
                : ctx.BarsSinceLowBreak_M5;

            double pullbackDepthR =
                dir == TradeDirection.Long
                    ? ctx.PullbackDepthRLong_M5
                    : ctx.PullbackDepthRShort_M5;

            ctx.Log?.Invoke($"[FX_MICRO STRUCT] barsSinceBreak={barsSinceBreak}");
            
            // -----------------------------------------------------
            // MARKET STATE FILTER
            // -----------------------------------------------------

            if (ctx.MarketState != null && ctx.MarketState.IsLowVol)
                return Invalid(ctx, dir, "LOW_VOL_BLOCK", score);
                
            // -----------------------------------------------------
            // MICRO COMPRESSION DETECTION
            // -----------------------------------------------------

            if (!TryComputeCompression(ctx, 4, out double hi, out double lo, out double rAtr))
                return Invalid(ctx, dir, "COMPRESSION_FAIL", score);

            ctx.Log?.Invoke($"[FX_MICRO RANGE] dir={dir} rATR={rAtr:F2}");

            if (rAtr > 1.8)
                return Invalid(ctx, dir, "RANGE_TOO_WIDE", score);

            if (rAtr < 0.9) score += 4;
            else if (rAtr < 1.2) score += 2;
            else if (rAtr < 1.6) score += 0;

            // -----------------------------------------------------
            // IMPULSE CONTEXT
            // -----------------------------------------------------

            if (ctx.HasImpulse_M5)
                score += 3;
            else
                score -= 2;

            if (ctx.IsAtrExpanding_M5)
                score += 2;

            // -----------------------------------------------------
            // HTF ALIGNMENT (soft)
            // -----------------------------------------------------

            if (ctx.FxHtfAllowedDirection == dir)
            {
                score += 5;
            }
            else if (ctx.FxHtfAllowedDirection != TradeDirection.None)
            {
                score -= 10;
            }

            // -----------------------------------------------------
            // BREAKOUT TRIGGER
            // -----------------------------------------------------

            bool breakout = false;

            if (ctx.HasBreakout_M1 &&
                ctx.BreakoutDirection == dir &&
                rAtr < 1.6 &&
                barsSinceBreak <= 2)
            {
                breakout = true;
            }

            bool continuationSignal = breakout;

            bool hasStructure =
                pullbackDepthR >= MinPullbackAtr;

            if (!hasStructure)
                setupScore -= 35;
            else
                setupScore += 15;

            bool hasContinuation =
                continuationSignal;

            if (hasContinuation)
                setupScore += 20;

            score += 6;

            // -----------------------------------------------------
            // ENTRY BAR QUALITY
            // -----------------------------------------------------

            int lastClosed = ctx.M5.Count - 2;
            if (lastClosed < 0)
                return Invalid(ctx, dir, "NO_LAST_BAR", score);

            var last = ctx.M5[lastClosed];

            double body = Math.Abs(last.Close - last.Open);
            double range = last.High - last.Low;
            bool strongCandle = range > 0 && body / range > 0.55;
            bool followThrough = continuationSignal;

            if (strongCandle)
                score += 2;

            if (breakout)
                triggerScore += 1;

            if (strongCandle)
                triggerScore += 1;

            if (followThrough)
                triggerScore += 2;

            // -----------------------------------------------------
            // STRUCTURE FRESHNESS
            // -----------------------------------------------------

            if (barsSinceBreak > 3)
                score -= 3;

            if (barsSinceBreak > 5)
                return Invalid(ctx, dir, "LATE_STRUCTURE", score);

            // -----------------------------------------------------
            // ADX ENERGY CHECK
            // -----------------------------------------------------

            if (TryGetDouble(ctx, "Adx_M5", out var adx))
            {
                if (adx < 14)
                    score -= 6;

                if (adx > 28)
                    score += 2;
            }

            // -----------------------------------------------------
            // FINAL SCORE GATE
            // -----------------------------------------------------

            int minScore = EntryDecisionPolicy.MinScoreThreshold;

            score = ApplyMandatoryEntryAdjustments(ctx, dir, score, false);
            score += setupScore;

            score += (int)Math.Round(triggerScore * 5);

            if (triggerScore == 0)
                score -= 15;

            bool minimalTrigger = breakout || strongCandle;
            if (!minimalTrigger)
                score -= 10;

            ctx.Log?.Invoke(
                $"[TRIGGER SCORE] breakout={(breakout ? 1 : 0)} strong={(strongCandle ? 1 : 0)} follow={(followThrough ? 1 : 0)} total={triggerScore:F0} finalScore={score}");

            if (setupScore <= 0)
                score = Math.Min(score, minScore - 10);

            ctx.Log?.Invoke($"[FX_MICRO FINAL] dir={dir} score={score} min={minScore}");

            if (score < minScore)
                return Invalid(ctx, dir, $"LOW_SCORE {score}<{minScore}", score);

            return Valid(ctx, dir, score, rAtr, hi, lo);
        }

        // -----------------------------------------------------
        // COMPRESSION DETECTOR
        // -----------------------------------------------------

        private static bool TryComputeCompression(
            EntryContext ctx,
            int bars,
            out double hi,
            out double lo,
            out double rangeAtr)
        {
            hi = double.MinValue;
            lo = double.MaxValue;

            if (ctx.M5.Count < bars + 3)
            {
                rangeAtr = 0;
                return false;
            }

            int lastClosed = ctx.M5.Count - 2;
            int first = lastClosed - bars + 1;

            for (int i = first; i <= lastClosed; i++)
            {
                var bar = ctx.M5[i];

                hi = Math.Max(hi, bar.High);
                lo = Math.Min(lo, bar.Low);
            }

            rangeAtr = (hi - lo) / ctx.AtrM5;

            return hi > lo;
        }

        // -----------------------------------------------------
        // RESULT HELPERS
        // -----------------------------------------------------

        private static EntryEvaluation Valid(
            EntryContext ctx,
            TradeDirection dir,
            int score,
            double rAtr,
            double hi,
            double lo)
            => new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = EntryType.FX_MicroStructure,
                Direction = dir,
                Score = score,
                IsValid = true,
                Reason =
                    $"FX_MICRO score={score} rATR={rAtr:F2} hi={hi:F5} lo={lo:F5}"
            };

        private static EntryEvaluation Invalid(
            EntryContext ctx,
            TradeDirection dir,
            string reason,
            int score)
            => new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.FX_MicroStructure,
                Direction = dir,
                Score = score,
                IsValid = false,
                Reason = $"{reason} raw={score}"
            };

        private static bool TryGetDouble(object obj, string prop, out double value)
        {
            value = 0;
            if (obj == null) return false;

            var p = obj.GetType().GetProperty(prop);
            if (p == null) return false;

            var v = p.GetValue(obj);
            if (v == null) return false;

            try
            {
                value = Convert.ToDouble(v);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            const int htfPenalty = 30;
            const int logicPenalty = 12;
            const int rangePenalty = 25;

            TradeDirection htfDirection = TradeDirection.None;
            double htfConfidence = 0.0;

            switch (SymbolRouting.ResolveInstrumentClass(ctx.Symbol))
            {
                case InstrumentClass.FX:
                    htfDirection = ctx.FxHtfAllowedDirection;
                    htfConfidence = ctx.FxHtfConfidence01;
                    break;
                case InstrumentClass.CRYPTO:
                    htfDirection = ctx.CryptoHtfAllowedDirection;
                    htfConfidence = ctx.CryptoHtfConfidence01;
                    break;
                case InstrumentClass.INDEX:
                    htfDirection = ctx.IndexHtfAllowedDirection;
                    htfConfidence = ctx.IndexHtfConfidence01;
                    break;
                case InstrumentClass.METAL:
                    htfDirection = ctx.MetalHtfAllowedDirection;
                    htfConfidence = ctx.MetalHtfConfidence01;
                    break;
            }

            if (htfDirection != TradeDirection.None && htfConfidence >= 0.70 && direction != htfDirection)
            {
                score -= htfPenalty;
                ctx.Log?.Invoke($"[ENTRY HTF ALIGN] dir={direction} htf={htfDirection} conf={htfConfidence:0.00} penalty={htfPenalty}");
            }

            var logicBias = ctx.LogicBiasDirection;
            var logicConfidence = ctx.LogicBiasConfidence;
            if (logicBias != TradeDirection.None && logicConfidence >= 60 && direction != logicBias)
            {
                score -= logicPenalty;
                ctx.Log?.Invoke($"[ENTRY LOGIC ALIGN] dir={direction} logic={logicBias} conf={logicConfidence} penalty={logicPenalty}");
            }

            if (applyTrendRegimePenalty && ctx.Adx_M5 < 15.0)
            {
                score -= rangePenalty;
                ctx.Log?.Invoke($"[ENTRY REGIME] adx={ctx.Adx_M5:0.0} penalty={rangePenalty}");
            }

            return score;
        }

    }
}
