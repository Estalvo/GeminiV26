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
    /// - a detector leíró állapotot ad vissza, NEM agresszív rejectet
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

            var longSide = EvaluateSide(ctx, rules, last, TradeDirection.Long, state.LongState);
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
            ctx.FlagBarsLong_M5 = longSide.FlagBars;
            ctx.FlagBarsShort_M5 = shortSide.FlagBars;

            ctx.FlagCompressionScoreLong_M5 = longSide.CompressionScore;
            ctx.FlagCompressionScoreShort_M5 = shortSide.CompressionScore;

            ctx.TransitionLong = BuildEvaluation(longSide);
            ctx.TransitionShort = BuildEvaluation(shortSide);

            ctx.HasImpulse_M5 = longSide.HasImpulse || shortSide.HasImpulse;

            ctx.BarsSinceImpulse_M5 = Math.Min(
                longSide.HasImpulse ? longSide.BarsSinceImpulse : 999,
                shortSide.HasImpulse ? shortSide.BarsSinceImpulse : 999);

            // 🚫 HARD RULE: Transition NEVER creates tradable signal
            ctx.TransitionValid = false;
            ctx.TransitionScoreBonus = 0;

            // 🚫 PURE NEUTRAL AGGREGATION (NO DIRECTION)
            ctx.Transition = new TransitionEvaluation
            {
                HasImpulse = ctx.HasImpulse_M5,
                HasPullback = longSide.HasPullback || shortSide.HasPullback,
                HasFlag = longSide.HasFlag || shortSide.HasFlag,
                BarsSinceImpulse = ctx.BarsSinceImpulse_M5 >= 999 ? -1 : ctx.BarsSinceImpulse_M5,
                PullbackBars = Math.Max(longSide.PullbackBars, shortSide.PullbackBars),
                FlagBars = Math.Max(longSide.FlagBars, shortSide.FlagBars),
                PullbackDepthR = Math.Max(longSide.PullbackDepthR, shortSide.PullbackDepthR),
                CompressionScore = Math.Max(longSide.CompressionScore, shortSide.CompressionScore),
                QualityScore = Math.Max(longSide.QualityScore, shortSide.QualityScore),

                IsValid = false,
                BonusScore = 0,
                Reason = "Neutral_NoProjection"
            };

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
                $"[TRANSITION][LONG] phase={longSide.Phase} " +
                $"impulse={longSide.HasImpulse.ToString().ToLowerInvariant()} barsSince={longSide.BarsSinceImpulse} " +
                $"pullback={longSide.HasPullback.ToString().ToLowerInvariant()} pbBars={longSide.PullbackBars} pbDepthR={longSide.PullbackDepthR:0.00} " +
                $"flagState={longSide.HasFlag.ToString().ToLowerInvariant()} flagBars={longSide.FlagBars} comp={longSide.CompressionScore:0.00} " +
                $"tradable={longSide.IsTradable.ToString().ToLowerInvariant()} score={longSide.QualityScore:0.00} bonus={longSide.BonusScore} reason={longSide.Reason}");

            ctx.Log?.Invoke(
                $"[TRANSITION][SHORT] phase={shortSide.Phase} " +
                $"impulse={shortSide.HasImpulse.ToString().ToLowerInvariant()} barsSince={shortSide.BarsSinceImpulse} " +
                $"pullback={shortSide.HasPullback.ToString().ToLowerInvariant()} pbBars={shortSide.PullbackBars} pbDepthR={shortSide.PullbackDepthR:0.00} " +
                $"flagState={shortSide.HasFlag.ToString().ToLowerInvariant()} flagBars={shortSide.FlagBars} comp={shortSide.CompressionScore:0.00} " +
                $"tradable={shortSide.IsTradable.ToString().ToLowerInvariant()} score={shortSide.QualityScore:0.00} bonus={shortSide.BonusScore} reason={shortSide.Reason}");

            ctx.Log?.Invoke(
                $"[TRACE][TRANSITION_STATE] symbol={ctx.Symbol} " +
                $"longImpulseSince={state.LongState.BarsSinceImpulse} longPullbackSince={state.LongState.BarsSincePullback} longFlagSince={state.LongState.BarsSinceFlag} " +
                $"shortImpulseSince={state.ShortState.BarsSinceImpulse} shortPullbackSince={state.ShortState.BarsSincePullback} shortFlagSince={state.ShortState.BarsSinceFlag}");

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
                IsValid = longSide.IsTradable || shortSide.IsTradable,
                BonusScore = Math.Max(longSide.BonusScore, shortSide.BonusScore),
                Reason = longSide.IsTradable && shortSide.IsTradable
                    ? "BothSidesTradable"
                    : longSide.IsTradable
                        ? "LongSideTradable"
                        : shortSide.IsTradable
                            ? "ShortSideTradable"
                            : "Neutral"
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
            bool hasImpulse = false;
            bool weakImpulse = false;
            int barsSinceImpulse = 999;

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

                hasImpulse = true;
                break;
            }

            barsSinceImpulse = hasImpulse
                ? last - impulseIndex
                : Math.Min(runtimeState.BarsSinceImpulse + 1, 999);

            if (hasImpulse && barsSinceImpulse > rules.MaxImpulseAge)
            {
                hasImpulse = false;
                impulseIndex = -1;
                impulseRange = 0.0;
                impulseStrength = 0.0;
            }

            weakImpulse = hasImpulse && impulseStrength < rules.MinImpulseStrength;
            if (weakImpulse)
            {
                hasImpulse = false;
                impulseIndex = -1;
                impulseRange = 0.0;
                impulseStrength = 0.0;
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
            bool pullbackForming = false;
            bool pullbackDeep = false;

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
                    pullbackForming = pullbackBars > 0;
                    pullbackDeep = pullbackDepthR > rules.MaxPullbackDepthR;
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
            bool decelerationPresent = false;
            bool compressionPresent = false;

            if (hasImpulse && hasPullback)
            {
                flagStart = pullbackEnd + 1;

                if (flagStart > pullbackEnd && flagStart <= last)
                {
                    flagBars = last - flagStart + 1;

                    if (flagBars > 0 && flagBars <= 50)
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
                }

                double strongImpulseThreshold = Math.Max(rules.MinImpulseStrength, 0.60);
                double maxPullbackDepth = rules.MaxPullbackDepthR;

                relaxedContinuation =
                    impulseStrength > strongImpulseThreshold &&
                    pullbackDepthR < maxPullbackDepth &&
                    ctx.MarketState != null &&
                    ctx.MarketState.Adx > rules.StrongAdxThreshold;
            }

            // =========================================================
            // CONTINUATION QUALITY GATE (deceleration + compression mandatory)
            // =========================================================
            int postImpulseStart = hasImpulse ? impulseIndex + 1 : -1;
            int postImpulseBars = (hasImpulse && postImpulseStart <= last)
                ? (last - postImpulseStart + 1)
                : 0;

            double impulseBody = hasImpulse
                ? Math.Abs(ctx.M5.ClosePrices[impulseIndex] - ctx.M5.OpenPrices[impulseIndex])
                : 0.0;
            double avgPostRange = 0.0;
            double avgPostBody = 0.0;
            double avgPostDirectionalBody = 0.0;
            int compressionBars = Math.Max(0, flagBars);
            double compressionRange = 0.0;
            bool noOpposingExpansion = true;

            if (postImpulseBars >= 1)
            {
                double sumRange = 0.0;
                double sumBody = 0.0;
                double sumDirectionalBody = 0.0;
                double compHigh = double.MinValue;
                double compLow = double.MaxValue;

                for (int i = postImpulseStart; i <= last; i++)
                {
                    double barRange = ctx.M5.HighPrices[i] - ctx.M5.LowPrices[i];
                    double barBody = Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]);
                    sumRange += barRange;
                    sumBody += barBody;

                    bool inContinuationDirection =
                        (direction == TradeDirection.Long && ctx.M5.ClosePrices[i] > ctx.M5.OpenPrices[i]) ||
                        (direction == TradeDirection.Short && ctx.M5.ClosePrices[i] < ctx.M5.OpenPrices[i]);

                    if (inContinuationDirection)
                        sumDirectionalBody += barBody;

                    if (!inContinuationDirection &&
                        barRange >= impulseRange * 0.70 &&
                        barBody >= Math.Max(impulseBody * 0.70, 0.0))
                    {
                        noOpposingExpansion = false;
                    }

                    compHigh = Math.Max(compHigh, ctx.M5.HighPrices[i]);
                    compLow = Math.Min(compLow, ctx.M5.LowPrices[i]);
                }

                avgPostRange = sumRange / postImpulseBars;
                avgPostBody = sumBody / postImpulseBars;
                avgPostDirectionalBody = sumDirectionalBody / postImpulseBars;
                compressionRange = (compHigh > compLow) ? (compHigh - compLow) : 0.0;
                compressionBars = Math.Max(compressionBars, postImpulseBars);
            }

            bool rangeDeceleration = impulseRange > 0 && avgPostRange <= impulseRange * 0.75;
            bool bodyDeceleration = impulseBody > 0 && avgPostBody <= impulseBody * 0.75;
            bool aggressionDeceleration = impulseBody > 0 && avgPostDirectionalBody <= impulseBody * 0.60;

            decelerationPresent =
                postImpulseBars >= 2 &&
                (rangeDeceleration || bodyDeceleration || aggressionDeceleration);

            bool compressionTightVsImpulse = impulseRange > 0 && compressionRange < impulseRange;
            bool compressionTightVsAtr = ctx.AtrM5 > 0 && compressionRange <= ctx.AtrM5 * 0.90;
            bool pullbackControlled =
                hasPullback &&
                !pullbackDeep &&
                pullbackDepthR <= rules.MaxPullbackDepthR;

            compressionPresent =
                compressionBars >= 2 &&
                compressionTightVsImpulse &&
                compressionTightVsAtr &&
                pullbackControlled &&
                noOpposingExpansion;

            // =========================================================
            // EARLY CONTINUATION / PHASE
            // =========================================================
            bool earlyContinuation =
                hasImpulse &&
                !hasPullback &&
                pullbackBars == 0 &&
                barsSinceImpulse <= 3 &&
                ctx.MarketState != null &&
                ctx.MarketState.IsTrend;

            string phase = BuildPhase(
                hasImpulse,
                hasPullback,
                hasFlag,
                earlyContinuation,
                pullbackForming,
                pullbackDeep,
                barsSinceImpulse);

            // =========================================================
            // TRADABLE STATE (leíró, nem agresszív detector döntés)
            // =========================================================
            bool continuationCandidate =
                hasImpulse &&
                hasPullback &&
                (hasFlag || relaxedContinuation);

            if (continuationCandidate)
            {
                ctx.Log?.Invoke(
                    $"[SETUP][CONT] impulseRange={impulseRange:0.00000} avgPostRange={avgPostRange:0.00000} avgPostBody={avgPostBody:0.00000} decel={decelerationPresent.ToString().ToLowerInvariant()}");
                ctx.Log?.Invoke(
                    $"[SETUP][CONT] compressionBars={compressionBars} compressionRange={compressionRange:0.00000} atr={ctx.AtrM5:0.00000} tight={compressionPresent.ToString().ToLowerInvariant()}");
            }

            bool isTradable =
                continuationCandidate &&
                decelerationPresent &&
                compressionPresent;

            if (continuationCandidate && !decelerationPresent)
                ctx.Log?.Invoke("[SETUP][CONT][REJECT] reason=NO_DECEL");

            if (continuationCandidate && !compressionPresent)
                ctx.Log?.Invoke("[SETUP][CONT][REJECT] reason=NO_COMPRESSION");

            if (continuationCandidate && decelerationPresent && compressionPresent)
                ctx.Log?.Invoke("[SETUP][CONT][ACCEPT] reason=DECEL+COMPRESSION_OK");

            double qualityScore = 0.0;
            int bonus = 0;

            if (hasImpulse)
            {
                double qualityScore01 =
                    (impulseStrength * 0.4) +
                    (compressionScore * 0.3) +
                    (pullbackQuality * 0.3);

                qualityScore = Math.Max(0.0, Math.Min(100.0, qualityScore01 * 100.0));

                bonus = isTradable
                    ? Clamp((int)(qualityScore01 * 10.0), 5, 18)
                    : 0;

                ctx.Log?.Invoke(
                    $"[SCORE][TRANSITION] raw={qualityScore01:0.0000} normalized={qualityScore:0.00}");
            }

            string reason = BuildReason(
                hasImpulse,
                hasPullback,
                hasFlag,
                earlyContinuation,
                relaxedContinuation,
                flagStructureBroken,
                weakImpulse,
                pullbackForming,
                pullbackDeep,
                compression,
                rules.MaxCompressionRatio,
                barsSinceImpulse);

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
                isTradable,
                qualityScore,
                bonus,
                reason,
                phase);
        }

        private static void ApplyLegacyProjection(
            EntryContext ctx,
            SideEvaluation side,
            TradeDirection direction)
        {
            // LEGACY DISABLED — DO NOT PROJECT DIRECTION

            ctx.TransitionValid = false;
            ctx.TransitionScoreBonus = 0;

            ctx.Transition = new TransitionEvaluation
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

                IsValid = false,
                BonusScore = 0,
                Reason = "LegacyDisabled"
            };

            ctx.Log?.Invoke("[TRANSITION_LEGACY_DISABLED]");
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
                IsValid = side.IsTradable,
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
            bool isTradable,
            double qualityScore,
            int bonus,
            string reason,
            string phase)
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

                IsTradable = isTradable,
                QualityScore = qualityScore,
                BonusScore = bonus,
                Reason = reason,
                Phase = phase
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

            public bool IsTradable { get; set; }
            public double QualityScore { get; set; }
            public int BonusScore { get; set; }
            public string Reason { get; set; }
            public string Phase { get; set; }
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

        private static string BuildPhase(
            bool hasImpulse,
            bool hasPullback,
            bool hasFlag,
            bool earlyContinuation,
            bool pullbackForming,
            bool pullbackDeep,
            int barsSinceImpulse)
        {
            if (!hasImpulse)
                return "NONE";

            if (earlyContinuation)
                return "EARLY_CONTINUATION";

            if (!hasPullback && barsSinceImpulse <= 3)
                return "IMPULSE_ONLY";

            if (!hasPullback && barsSinceImpulse > 3)
                return "IMPULSE_DECAY";

            if (pullbackDeep)
                return "PULLBACK_DEEP";

            if (pullbackForming && !hasFlag)
                return "PULLBACK_FORMING";

            if (hasFlag)
                return "FLAG_READY";

            return "NEUTRAL";
        }

        private static string BuildReason(
            bool hasImpulse,
            bool hasPullback,
            bool hasFlag,
            bool earlyContinuation,
            bool relaxedContinuation,
            bool flagStructureBroken,
            bool weakImpulse,
            bool pullbackForming,
            bool pullbackDeep,
            double compression,
            double maxCompression,
            int barsSinceImpulse)
        {
            if (weakImpulse)
                return "WEAK_IMPULSE";

            if (!hasImpulse)
                return "MISSING_IMPULSE";

            if (earlyContinuation)
                return "EARLY_CONTINUATION";

            if (!hasPullback)
            {
                if (barsSinceImpulse <= 3)
                    return "PULLBACK_NOT_FORMED";

                return "IMPULSE_DECAY";
            }

            if (pullbackDeep)
                return "PULLBACK_DEEP";

            if (relaxedContinuation)
                return "RELAXED_CONTINUATION";

            if (!hasFlag)
            {
                if (flagStructureBroken)
                    return "FLAG_STRUCTURE_BREAK";

                if (compression > maxCompression)
                    return "WEAK_COMPRESSION";

                if (pullbackForming)
                    return "FLAG_NOT_FORMED";

                return "NEUTRAL";
            }

            return "OK";
        }
    }
}
