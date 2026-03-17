using System;
using System.Collections.Generic;
using GeminiV26.Core;

namespace GeminiV26.Core.Entry
{
    /// <summary>
    /// Direction-agnostic, 2-sided transition detector.
    ///
    /// FONTOS:
    /// - a detector NEM dönt irányról
    /// - a detector mindkét oldalra külön számol:
    ///     - long transition
    ///     - short transition
    /// - az entry layer dönti el, hogy ebből lesz-e valid long / short setup
    /// </summary>
    public sealed class TransitionDetector
    {
        private readonly Dictionary<string, TransitionRuntimeState> _stateBySymbol =
            new(StringComparer.OrdinalIgnoreCase);

        public TransitionEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || ctx.M5 == null || ctx.M5.Count < 12 || ctx.AtrM5 <= 0)
            {
                ResetContext(ctx);
                return Invalid("InsufficientData");
            }

            string symbol = string.IsNullOrWhiteSpace(ctx.Symbol) ? "__DEFAULT__" : ctx.Symbol;

            if (!_stateBySymbol.TryGetValue(symbol, out var state))
            {
                state = new TransitionRuntimeState();
                _stateBySymbol[symbol] = state;
            }

            var rules = TransitionRules.ForInstrument(ResolveInstrumentType(ctx));
            int last = ctx.M5.Count - 2;

            // =========================================================
            // LONG SIDE
            // =========================================================
            var longSide = EvaluateSide(ctx, rules, last, TradeDirection.Long, state.LongState);

            // =========================================================
            // SHORT SIDE
            // =========================================================
            var shortSide = EvaluateSide(ctx, rules, last, TradeDirection.Short, state.ShortState);

            // =========================================================
            // WRITE CONTEXT (2-sided truth)
            // =========================================================
            ctx.HasImpulseLong_M5 = longSide.HasImpulse;
            ctx.HasImpulseShort_M5 = shortSide.HasImpulse;

            ctx.BarsSinceImpulseLong_M5 = longSide.HasImpulse ? longSide.BarsSinceImpulse : 999;
            ctx.BarsSinceImpulseShort_M5 = shortSide.HasImpulse ? shortSide.BarsSinceImpulse : 999;

            ctx.HasPullbackLong_M5 = longSide.HasPullback;
            ctx.HasPullbackShort_M5 = shortSide.HasPullback;

            ctx.PullbackBarsLong_M5 = longSide.PullbackBars;
            ctx.PullbackBarsShort_M5 = shortSide.PullbackBars;

            ctx.PullbackDepthRLong_M5 = longSide.PullbackDepthR;
            ctx.PullbackDepthRShort_M5 = shortSide.PullbackDepthR;

            ctx.HasFlagLong_M5 = longSide.HasFlag;
            ctx.HasFlagShort_M5 = shortSide.HasFlag;

            ctx.FlagBarsLong_M5 = longSide.FlagBars;
            ctx.FlagBarsShort_M5 = shortSide.FlagBars;

            ctx.FlagCompressionScoreLong_M5 = longSide.CompressionScore;
            ctx.FlagCompressionScoreShort_M5 = shortSide.CompressionScore;

            ctx.TransitionLong = BuildEvaluation(longSide);
            ctx.TransitionShort = BuildEvaluation(shortSide);

            // =========================================================
            // Backward compatibility layer
            // =========================================================
            // Régi mezők: itt NEM döntünk irányról agresszíven.
            // Ha csak az egyik oldal valid, azt tükrözzük vissza.
            // Ha mindkettő valid vagy egyik sem, maradjon neutrális.
            if (longSide.IsValid && !shortSide.IsValid)
            {
                ApplyLegacyProjection(ctx, longSide, TradeDirection.Long);
            }
            else if (!longSide.IsValid && shortSide.IsValid)
            {
                ApplyLegacyProjection(ctx, shortSide, TradeDirection.Short);
            }
            else
            {
                // neutral / ambiguous
                ctx.HasImpulse_M5 = longSide.HasImpulse || shortSide.HasImpulse;
                ctx.BarsSinceImpulse_M5 = Math.Min(
                    longSide.HasImpulse ? longSide.BarsSinceImpulse : 999,
                    shortSide.HasImpulse ? shortSide.BarsSinceImpulse : 999);

                ctx.TransitionValid = false;
                ctx.TransitionScoreBonus = 0;
                ctx.Transition = new TransitionEvaluation
                {
                    HasImpulse = ctx.HasImpulse_M5,
                    HasPullback = false,
                    HasFlag = false,
                    BarsSinceImpulse = ctx.BarsSinceImpulse_M5 >= 999 ? -1 : ctx.BarsSinceImpulse_M5,
                    PullbackBars = 0,
                    FlagBars = 0,
                    PullbackDepthR = 0.0,
                    CompressionScore = 0.0,
                    QualityScore = 0.0,
                    IsValid = false,
                    BonusScore = 0,
                    Reason = longSide.IsValid && shortSide.IsValid
                        ? "BothSidesValid"
                        : "NoDirectionalConsensus"
                };
            }

            // =========================================================
            // PullbackDepthAtr backward fill
            // =========================================================
            double maxDepthAtr = 0.0;

            if (longSide.HasPullback && ctx.AtrM5 > 0 && longSide.ImpulseRange > 0)
            {
                double impulseAtrLong = longSide.ImpulseRange / ctx.AtrM5;
                maxDepthAtr = Math.Max(maxDepthAtr, longSide.PullbackDepthR * impulseAtrLong);
            }

            if (shortSide.HasPullback && ctx.AtrM5 > 0 && shortSide.ImpulseRange > 0)
            {
                double impulseAtrShort = shortSide.ImpulseRange / ctx.AtrM5;
                maxDepthAtr = Math.Max(maxDepthAtr, shortSide.PullbackDepthR * impulseAtrShort);
            }

            if (maxDepthAtr > 0)
                ctx.PullbackDepthAtr_M5 = Math.Max(ctx.PullbackDepthAtr_M5, maxDepthAtr);

            // =========================================================
            // Logs
            // =========================================================
            ctx.Log?.Invoke(
                $"[TRANSITION][LONG] impulse={longSide.HasImpulse.ToString().ToLowerInvariant()} barsSince={longSide.BarsSinceImpulse} " +
                $"pullback={longSide.HasPullback.ToString().ToLowerInvariant()} pbBars={longSide.PullbackBars} pbDepthR={longSide.PullbackDepthR:0.00} " +
                $"flag={longSide.HasFlag.ToString().ToLowerInvariant()} flagBars={longSide.FlagBars} comp={longSide.CompressionScore:0.00} " +
                $"valid={longSide.IsValid.ToString().ToLowerInvariant()} score={longSide.QualityScore:0.00} bonus={longSide.BonusScore} reason={longSide.Reason}");

            ctx.Log?.Invoke(
                $"[TRANSITION][SHORT] impulse={shortSide.HasImpulse.ToString().ToLowerInvariant()} barsSince={shortSide.BarsSinceImpulse} " +
                $"pullback={shortSide.HasPullback.ToString().ToLowerInvariant()} pbBars={shortSide.PullbackBars} pbDepthR={shortSide.PullbackDepthR:0.00} " +
                $"flag={shortSide.HasFlag.ToString().ToLowerInvariant()} flagBars={shortSide.FlagBars} comp={shortSide.CompressionScore:0.00} " +
                $"valid={shortSide.IsValid.ToString().ToLowerInvariant()} score={shortSide.QualityScore:0.00} bonus={shortSide.BonusScore} reason={shortSide.Reason}");

            ctx.Log?.Invoke(
                $"[TRACE][TRANSITION_STATE] symbol={ctx.Symbol} " +
                $"longImpulseSince={state.LongState.BarsSinceImpulse} longPullbackSince={state.LongState.BarsSincePullback} longFlagSince={state.LongState.BarsSinceFlag} " +
                $"shortImpulseSince={state.ShortState.BarsSinceImpulse} shortPullbackSince={state.ShortState.BarsSincePullback} shortFlagSince={state.ShortState.BarsSinceFlag}");

            // =========================================================
            // Return value: neutral aggregate
            // =========================================================
            return new TransitionEvaluation
            {
                HasImpulse = longSide.HasImpulse || shortSide.HasImpulse,
                HasPullback = longSide.HasPullback || shortSide.HasPullback,
                HasFlag = longSide.HasFlag || shortSide.HasFlag,
                BarsSinceImpulse = Math.Min(
                    longSide.HasImpulse ? longSide.BarsSinceImpulse : 999,
                    shortSide.HasImpulse ? shortSide.BarsSinceImpulse : 999),
                PullbackBars = Math.Max(longSide.PullbackBars, shortSide.PullbackBars),
                FlagBars = Math.Max(longSide.FlagBars, shortSide.FlagBars),
                PullbackDepthR = Math.Max(longSide.PullbackDepthR, shortSide.PullbackDepthR),
                CompressionScore = Math.Max(longSide.CompressionScore, shortSide.CompressionScore),
                QualityScore = Math.Max(longSide.QualityScore, shortSide.QualityScore),
                IsValid = longSide.IsValid || shortSide.IsValid,
                BonusScore = Math.Max(longSide.BonusScore, shortSide.BonusScore),
                Reason = longSide.IsValid && shortSide.IsValid
                    ? "BothSidesValid"
                    : longSide.IsValid
                        ? "LongOnlyValid"
                        : shortSide.IsValid
                            ? "ShortOnlyValid"
                            : "NoValidTransition"
            };
        }

        private SideEvaluation EvaluateSide(
            EntryContext ctx,
            dynamic rules,
            int last,
            TradeDirection direction,
            SideRuntimeState runtimeState)
        {
            int impulseIndex = -1;
            double impulseRange = 0.0;
            double impulseStrength = 0.0;

            // =========================================================
            // IMPULSE
            // =========================================================
            for (int i = last; i >= Math.Max(1, last - rules.MaxImpulseAge); i--)
            {
                double range = ctx.M5.HighPrices[i] - ctx.M5.LowPrices[i];
                double body = Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]);
                double bodyRatio = range > 0 ? body / range : 0.0;

                if (range <= ctx.AtrM5 * rules.ImpulseMultiplier || bodyRatio < rules.MinImpulseBodyRatio)
                    continue;

                bool bullishImpulse =
                    ctx.M5.ClosePrices[i] > ctx.M5.OpenPrices[i] &&
                    ctx.M5.ClosePrices[i] > ctx.M5.HighPrices[i - 1];

                bool bearishImpulse =
                    ctx.M5.ClosePrices[i] < ctx.M5.OpenPrices[i] &&
                    ctx.M5.ClosePrices[i] < ctx.M5.LowPrices[i - 1];

                bool matchesDirection =
                    (direction == TradeDirection.Long && bullishImpulse) ||
                    (direction == TradeDirection.Short && bearishImpulse);

                if (!matchesDirection)
                    continue;

                impulseIndex = i;
                impulseRange = range;

                double normalizationAtr = ctx.AtrM5 * rules.ImpulseNormalizationAtrFactor;
                impulseStrength = normalizationAtr > 0
                    ? Clamp01(range / normalizationAtr)
                    : 0.0;

                break;
            }

            bool hasImpulse = impulseIndex >= 0;
            int barsSinceImpulse = hasImpulse
                ? last - impulseIndex
                : Math.Min(runtimeState.BarsSinceImpulse + 1, 999);

            if (hasImpulse && barsSinceImpulse > rules.MaxImpulseAge)
            {
                hasImpulse = false;
                impulseIndex = -1;
                impulseRange = 0.0;
                impulseStrength = 0.0;
            }

            bool weakImpulse = hasImpulse && impulseStrength < rules.MinImpulseStrength;
            if (weakImpulse)
            {
                hasImpulse = false;
                impulseIndex = -1;
                impulseRange = 0.0;
            }

            // =========================================================
            // PULLBACK
            // =========================================================
            int pullbackStart = hasImpulse ? impulseIndex + 1 : -1;
            int pullbackEnd = -1;
            int pullbackBars = 0;
            double pullbackDepthR = 0.0;
            double pullbackQuality = 0.0;
            bool trendAlignmentMaintained = false;
            bool hasPullback = false;

            if (hasImpulse && pullbackStart <= last)
            {
                pullbackEnd = FindPullbackEnd(ctx, pullbackStart, last, direction, rules.MinPullbackBars);
                pullbackBars = pullbackEnd >= pullbackStart
                    ? (pullbackEnd - pullbackStart + 1)
                    : 0;

                if (pullbackBars > 0 && impulseRange > 0)
                {
                    if (direction == TradeDirection.Long)
                    {
                        double pullbackLow = MinLow(ctx, pullbackStart, pullbackEnd);
                        double impulseHigh = ctx.M5.HighPrices[impulseIndex];

                        pullbackDepthR = Math.Abs(impulseHigh - pullbackLow) / impulseRange;
                        trendAlignmentMaintained = pullbackLow > ctx.M5.LowPrices[impulseIndex];
                    }
                    else
                    {
                        double pullbackHigh = MaxHigh(ctx, pullbackStart, pullbackEnd);
                        double impulseLow = ctx.M5.LowPrices[impulseIndex];

                        pullbackDepthR = Math.Abs(pullbackHigh - impulseLow) / impulseRange;
                        trendAlignmentMaintained = pullbackHigh < ctx.M5.HighPrices[impulseIndex];
                    }

                    pullbackQuality = Clamp01(1.0 - Math.Abs(pullbackDepthR - rules.OptimalPullbackDepthR));
                }

                hasPullback =
                    pullbackBars >= rules.MinPullbackBars &&
                    pullbackDepthR <= rules.MaxPullbackDepthR &&
                    trendAlignmentMaintained;
            }

            // =========================================================
            // FLAG
            // =========================================================
            int flagStart = -1;
            int flagBars = 0;
            double compression = 1.0;
            double compressionScore = 0.0;
            bool noStructureBreak = false;
            bool hasFlag = false;
            bool flagStructureBroken = false;
            bool relaxedContinuation = false;

            if (hasImpulse && hasPullback)
            {
                flagStart = pullbackEnd + 1;

                if (flagStart <= pullbackEnd || flagStart > last)
                {
                    UpdateRuntime(runtimeState, hasImpulse, hasPullback, false);
                    return BuildSideResult(
                        direction, hasImpulse, barsSinceImpulse, impulseRange, impulseStrength,
                        hasPullback, pullbackBars, pullbackDepthR, pullbackQuality,
                        false, 0, 0.0, 0.0, false, false, 0.0, 0,
                        "FLAG_NOT_DETECTED");
                }

                flagBars = last - flagStart + 1;

                if (flagBars > 50)
                {
                    UpdateRuntime(runtimeState, hasImpulse, hasPullback, false);
                    return BuildSideResult(
                        direction, hasImpulse, barsSinceImpulse, impulseRange, impulseStrength,
                        hasPullback, pullbackBars, pullbackDepthR, pullbackQuality,
                        false, flagBars, 0.0, 0.0, false, false, 0.0, 0,
                        "FLAG_INVALID_BAR_COUNT");
                }

                if (flagBars > 0)
                {
                    double avgRange = AverageRange(ctx, flagStart, last);
                    compression = impulseRange > 0 ? avgRange / impulseRange : 1.0;
                    compressionScore = Clamp01(1.0 - compression);

                    noStructureBreak = ValidateNoStructureBreak(
                        ctx, flagStart, last, direction, pullbackStart, pullbackEnd);

                    flagStructureBroken = !noStructureBreak;

                    hasFlag =
                        flagBars <= rules.MaxFlagBars &&
                        compression <= rules.MaxCompressionRatio &&
                        noStructureBreak;
                }

                double strongImpulseThreshold = Math.Max(rules.MinImpulseStrength, 0.60);
                double maxPullbackDepth = Math.Min(rules.MaxPullbackDepthR, 0.50);

                relaxedContinuation =
                    impulseStrength > strongImpulseThreshold &&
                    pullbackDepthR < maxPullbackDepth &&
                    ctx.MarketState != null &&
                    ctx.MarketState.Adx > rules.StrongAdxThreshold;
            }

            // =========================================================
            // FINAL VALIDITY
            // =========================================================
            bool isValid = hasImpulse && hasPullback && (hasFlag || relaxedContinuation);

            double qualityScore = 0.0;
            int bonus = 0;

            if (isValid)
            {
                qualityScore =
                    (impulseStrength * 0.4) +
                    (compressionScore * 0.3) +
                    (pullbackQuality * 0.3);

                bonus = Clamp((int)(qualityScore * 10.0), 5, 18);
            }

            string reason = isValid
                ? (hasFlag ? "OK" : "OK_RELAXED_CONTINUATION")
                : BuildReason(
                    hasImpulse,
                    hasPullback,
                    hasFlag,
                    flagStructureBroken,
                    weakImpulse,
                    pullbackDepthR,
                    rules.MaxPullbackDepthR,
                    compression,
                    rules.MaxCompressionRatio);

            UpdateRuntime(runtimeState, hasImpulse, hasPullback, hasFlag);

            return BuildSideResult(
                direction,
                hasImpulse,
                barsSinceImpulse,
                impulseRange,
                impulseStrength,
                hasPullback,
                pullbackBars,
                pullbackDepthR,
                pullbackQuality,
                hasFlag,
                flagBars,
                compression,
                compressionScore,
                relaxedContinuation,
                isValid,
                qualityScore,
                bonus,
                reason);
        }

        private static void ApplyLegacyProjection(
            EntryContext ctx,
            SideEvaluation side,
            TradeDirection direction)
        {
            ctx.HasImpulse_M5 = side.HasImpulse;
            ctx.BarsSinceImpulse_M5 = side.HasImpulse ? side.BarsSinceImpulse : 999;

            ctx.TransitionValid = side.IsValid;
            ctx.TransitionScoreBonus = side.BonusScore;
            ctx.Transition = BuildEvaluation(side);

            // Legacy fields downstreamnak
            ctx.ImpulseDirection = direction;
            ctx.TrendDirection = direction;
        }

        private static TransitionEvaluation BuildEvaluation(SideEvaluation side)
        {
            return new TransitionEvaluation
            {
                HasImpulse = side.HasImpulse,
                HasPullback = side.HasPullback,
                HasFlag = side.HasFlag,
                BarsSinceImpulse = side.HasImpulse ? side.BarsSinceImpulse : -1,
                PullbackBars = side.PullbackBars,
                FlagBars = side.FlagBars,
                PullbackDepthR = side.PullbackDepthR,
                CompressionScore = side.CompressionScore,
                QualityScore = side.QualityScore,
                IsValid = side.IsValid,
                BonusScore = side.BonusScore,
                Reason = side.Reason
            };
        }

        private static SideEvaluation BuildSideResult(
            TradeDirection direction,
            bool hasImpulse,
            int barsSinceImpulse,
            double impulseRange,
            double impulseStrength,
            bool hasPullback,
            int pullbackBars,
            double pullbackDepthR,
            double pullbackQuality,
            bool hasFlag,
            int flagBars,
            double compression,
            double compressionScore,
            bool relaxedContinuation,
            bool isValid,
            double qualityScore,
            int bonus,
            string reason)
        {
            return new SideEvaluation
            {
                Direction = direction,
                HasImpulse = hasImpulse,
                BarsSinceImpulse = hasImpulse ? barsSinceImpulse : 999,
                ImpulseRange = impulseRange,
                ImpulseStrength = impulseStrength,

                HasPullback = hasPullback,
                PullbackBars = pullbackBars,
                PullbackDepthR = pullbackDepthR,
                PullbackQuality = pullbackQuality,

                HasFlag = hasFlag,
                FlagBars = flagBars,
                Compression = compression,
                CompressionScore = compressionScore,
                RelaxedContinuation = relaxedContinuation,

                IsValid = isValid,
                QualityScore = qualityScore,
                BonusScore = bonus,
                Reason = reason
            };
        }

        private static void UpdateRuntime(
            SideRuntimeState state,
            bool hasImpulse,
            bool hasPullback,
            bool hasFlag)
        {
            state.BarsSinceImpulse = hasImpulse
                ? 0
                : Math.Min(state.BarsSinceImpulse + 1, 999);

            state.BarsSincePullback = hasPullback
                ? 0
                : Math.Min(state.BarsSincePullback + 1, 999);

            state.BarsSinceFlag = hasFlag
                ? 0
                : Math.Min(state.BarsSinceFlag + 1, 999);
        }

        private static void ResetContext(EntryContext ctx)
        {
            if (ctx == null)
                return;

            ctx.HasImpulse_M5 = false;
            ctx.BarsSinceImpulse_M5 = 999;

            ctx.TransitionValid = false;
            ctx.TransitionScoreBonus = 0;
            ctx.Transition = Invalid("InsufficientData");

            ctx.HasImpulseLong_M5 = false;
            ctx.HasImpulseShort_M5 = false;

            ctx.BarsSinceImpulseLong_M5 = 999;
            ctx.BarsSinceImpulseShort_M5 = 999;

            ctx.HasPullbackLong_M5 = false;
            ctx.HasPullbackShort_M5 = false;

            ctx.PullbackBarsLong_M5 = 0;
            ctx.PullbackBarsShort_M5 = 0;

            ctx.PullbackDepthRLong_M5 = 0.0;
            ctx.PullbackDepthRShort_M5 = 0.0;

            ctx.HasFlagLong_M5 = false;
            ctx.HasFlagShort_M5 = false;

            ctx.FlagBarsLong_M5 = 0;
            ctx.FlagBarsShort_M5 = 0;

            ctx.FlagCompressionScoreLong_M5 = 0.0;
            ctx.FlagCompressionScoreShort_M5 = 0.0;

            ctx.TransitionLong = Invalid("InsufficientData");
            ctx.TransitionShort = Invalid("InsufficientData");
        }

        private sealed class TransitionRuntimeState
        {
            public SideRuntimeState LongState { get; } = new();
            public SideRuntimeState ShortState { get; } = new();
        }

        private sealed class SideRuntimeState
        {
            public int BarsSinceImpulse { get; set; } = 999;
            public int BarsSincePullback { get; set; } = 999;
            public int BarsSinceFlag { get; set; } = 999;
        }

        private sealed class SideEvaluation
        {
            public TradeDirection Direction { get; set; }

            public bool HasImpulse { get; set; }
            public int BarsSinceImpulse { get; set; } = 999;
            public double ImpulseRange { get; set; }
            public double ImpulseStrength { get; set; }

            public bool HasPullback { get; set; }
            public int PullbackBars { get; set; }
            public double PullbackDepthR { get; set; }
            public double PullbackQuality { get; set; }

            public bool HasFlag { get; set; }
            public int FlagBars { get; set; }
            public double Compression { get; set; }
            public double CompressionScore { get; set; }
            public bool RelaxedContinuation { get; set; }

            public bool IsValid { get; set; }
            public double QualityScore { get; set; }
            public int BonusScore { get; set; }
            public string Reason { get; set; }
        }

        private static int FindPullbackEnd(
            EntryContext ctx,
            int start,
            int end,
            TradeDirection impulseDirection,
            int minBars)
        {
            int bars = 0;

            for (int i = start; i <= end; i++)
            {
                double dClose = ctx.M5.ClosePrices[i] - ctx.M5.ClosePrices[i - 1];

                bool counterMove = impulseDirection == TradeDirection.Long
                    ? dClose <= 0
                    : dClose >= 0;

                if (counterMove)
                {
                    bars++;
                    continue;
                }

                if (bars >= minBars)
                    return i - 1;

                bars = 0;
            }

            return bars > 0 ? end : -1;
        }

        private static bool ValidateNoStructureBreak(
            EntryContext ctx,
            int flagStart,
            int flagEnd,
            TradeDirection impulseDirection,
            int pullbackStart,
            int pullbackEnd)
        {
            if (flagStart > flagEnd || pullbackStart < 0 || pullbackEnd < pullbackStart)
                return false;

            double pullbackLow = MinLow(ctx, pullbackStart, pullbackEnd);
            double pullbackHigh = MaxHigh(ctx, pullbackStart, pullbackEnd);

            if (impulseDirection == TradeDirection.Long)
            {
                for (int i = flagStart; i <= flagEnd; i++)
                {
                    if (ctx.M5.LowPrices[i] < pullbackLow)
                        return false;
                }

                return true;
            }

            for (int i = flagStart; i <= flagEnd; i++)
            {
                if (ctx.M5.HighPrices[i] > pullbackHigh)
                    return false;
            }

            return true;
        }

        private static TransitionEvaluation Invalid(
            string reason,
            int pullbackBars = 0,
            int flagBars = 0,
            double pullbackDepthR = 0.0,
            double compressionScore = 0.0)
        {
            return new TransitionEvaluation
            {
                HasImpulse = false,
                HasPullback = false,
                HasFlag = false,
                BarsSinceImpulse = -1,
                PullbackBars = pullbackBars,
                FlagBars = flagBars,
                PullbackDepthR = pullbackDepthR,
                CompressionScore = compressionScore,
                QualityScore = 0.0,
                IsValid = false,
                BonusScore = 0,
                Reason = reason
            };
        }

        private static double AverageRange(EntryContext ctx, int start, int end)
        {
            if (start > end)
                return 0.0;

            double total = 0.0;
            int count = 0;

            for (int i = start; i <= end; i++)
            {
                total += ctx.M5.HighPrices[i] - ctx.M5.LowPrices[i];
                count++;
            }

            return count > 0 ? total / count : 0.0;
        }

        private static double MinLow(EntryContext ctx, int start, int end)
        {
            double low = double.MaxValue;

            for (int i = start; i <= end; i++)
                low = Math.Min(low, ctx.M5.LowPrices[i]);

            return low;
        }

        private static double MaxHigh(EntryContext ctx, int start, int end)
        {
            double high = double.MinValue;

            for (int i = start; i <= end; i++)
                high = Math.Max(high, ctx.M5.HighPrices[i]);

            return high;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
                return min;

            if (value > max)
                return max;

            return value;
        }

        private static double Clamp01(double value)
        {
            if (value < 0.0)
                return 0.0;

            if (value > 1.0)
                return 1.0;

            return value;
        }

        private static InstrumentType ResolveInstrumentType(EntryContext ctx)
        {
            var instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx?.Symbol);

            return instrumentClass switch
            {
                InstrumentClass.CRYPTO => InstrumentType.CRYPTO,
                InstrumentClass.METAL => InstrumentType.METAL,
                InstrumentClass.INDEX => InstrumentType.INDEX,
                _ => InstrumentType.FX
            };
        }

        private static string BuildReason(
            bool hasImpulse,
            bool hasPullback,
            bool hasFlag,
            bool flagStructureBroken,
            bool weakImpulse,
            double pullbackDepthR,
            double maxPullbackDepthR,
            double compression,
            double maxCompression)
        {
            if (weakImpulse)
                return "WeakImpulse";

            if (!hasImpulse)
                return "MissingImpulse";

            if (!hasPullback)
            {
                if (pullbackDepthR > maxPullbackDepthR)
                    return "PullbackTooDeep";

                return "InvalidPullback";
            }

            if (!hasFlag)
            {
                if (flagStructureBroken)
                    return "FlagStructureBreak";

                if (compression > maxCompression)
                    return "WeakCompression";

                return "InvalidFlag";
            }

            return "Unknown";
        }
    }
}