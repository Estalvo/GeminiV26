using System;

namespace GeminiV26.Core.Entry
{
    public sealed class TransitionDetector
    {
        public TransitionEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || ctx.M5 == null || ctx.M5.Count < 12 || ctx.AtrM5 <= 0)
            {
                return new TransitionEvaluation { Reason = "InsufficientData" };
            }

            var rules = TransitionRules.ForSymbol(ctx.Symbol);
            int last = ctx.M5.Count - 2;

            int impulseIndex = -1;
            TradeDirection impulseDirection = TradeDirection.None;
            double impulseRange = 0.0;
            double impulseStrength = 0.0;

            for (int i = last; i >= Math.Max(1, last - rules.MaxImpulseAge); i--)
            {
                double range = ctx.M5.HighPrices[i] - ctx.M5.LowPrices[i];
                double body = Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]);
                double bodyRatio = range > 0 ? body / range : 0.0;

                if (range <= ctx.AtrM5 * rules.ImpulseMultiplier || bodyRatio < rules.MinImpulseBodyRatio)
                    continue;

                bool bullishImpulse = ctx.M5.ClosePrices[i] > ctx.M5.OpenPrices[i] && ctx.M5.ClosePrices[i] > ctx.M5.HighPrices[i - 1];
                bool bearishImpulse = ctx.M5.ClosePrices[i] < ctx.M5.OpenPrices[i] && ctx.M5.ClosePrices[i] < ctx.M5.LowPrices[i - 1];

                if (!bullishImpulse && !bearishImpulse)
                    continue;

                impulseIndex = i;
                impulseDirection = bullishImpulse ? TradeDirection.Long : TradeDirection.Short;
                impulseRange = range;
                impulseStrength = ctx.AtrM5 > 0 ? range / ctx.AtrM5 : 0.0;
                break;
            }

            bool hasImpulse = impulseIndex >= 0;
            int barsSinceImpulse = hasImpulse ? last - impulseIndex : int.MaxValue;
            if (hasImpulse && barsSinceImpulse > rules.MaxImpulseAge)
            {
                hasImpulse = false;
                impulseIndex = -1;
                impulseDirection = TradeDirection.None;
                impulseRange = 0.0;
                impulseStrength = 0.0;
            }

            ctx.Log?.Invoke($"[TRANSITION][IMPULSE] detected={hasImpulse.ToString().ToLowerInvariant()} barsSince={barsSinceImpulse} strength={impulseStrength:0.00}");

            int pullbackStart = hasImpulse ? impulseIndex + 1 : -1;
            int pullbackEnd = -1;
            int pullbackBars = 0;
            double pullbackDepthR = 0.0;
            double pullbackQuality = 0.0;
            bool trendAlignmentMaintained = false;
            bool hasPullback = false;

            if (hasImpulse && pullbackStart <= last)
            {
                pullbackEnd = FindPullbackEnd(ctx, pullbackStart, last, impulseDirection, rules.MinPullbackBars);
                pullbackBars = pullbackEnd >= pullbackStart ? (pullbackEnd - pullbackStart + 1) : 0;

                if (pullbackBars > 0 && impulseRange > 0)
                {
                    if (impulseDirection == TradeDirection.Long)
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

                    pullbackQuality = Clamp01(1.0 - pullbackDepthR);
                }

                hasPullback = pullbackBars >= rules.MinPullbackBars
                    && pullbackDepthR <= rules.MaxPullbackDepthR
                    && pullbackDepthR <= 0.5
                    && trendAlignmentMaintained;
            }

            ctx.Log?.Invoke($"[TRANSITION][PULLBACK] bars={pullbackBars} depthR={pullbackDepthR:0.00}");

            int flagStart = pullbackEnd + 1;
            int flagBars = flagStart <= last ? (last - flagStart + 1) : 0;
            double compression = 1.0;
            double compressionScore = 0.0;
            bool noStructureBreak = false;
            bool hasFlag = false;
            bool flagStructureBroken = false;

            if (hasImpulse && hasPullback && flagBars > 0)
            {
                double avgRange = AverageRange(ctx, flagStart, last);
                compression = impulseRange > 0 ? avgRange / impulseRange : 1.0;
                compressionScore = Clamp01(1.0 - compression);

                noStructureBreak = ValidateNoStructureBreak(ctx, flagStart, last, impulseDirection, pullbackStart, pullbackEnd);
                flagStructureBroken = !noStructureBreak;

                hasFlag = flagBars <= rules.MaxFlagBars
                    && compression <= rules.MaxCompressionRatio
                    && noStructureBreak;
            }

            ctx.Log?.Invoke($"[TRANSITION][FLAG] bars={flagBars} compression={compression:0.00}");

            bool isValid = hasImpulse && hasPullback && hasFlag;

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
                ? "OK"
                : BuildReason(hasImpulse, hasPullback, hasFlag, flagStructureBroken, pullbackDepthR, rules.MaxPullbackDepthR, compression, rules.MaxCompressionRatio);

            ctx.Log?.Invoke($"[TRANSITION][QUALITY] impulse={impulseStrength:0.00} compression={compressionScore:0.00} pullback={pullbackQuality:0.00} score={qualityScore:0.00} bonus={bonus}");
            ctx.Log?.Invoke($"[TRANSITION][DECISION] valid={isValid.ToString().ToLowerInvariant()} bonus={bonus}");

            return new TransitionEvaluation
            {
                HasImpulse = hasImpulse,
                HasPullback = hasPullback,
                HasFlag = hasFlag,
                BarsSinceImpulse = hasImpulse ? barsSinceImpulse : -1,
                PullbackBars = pullbackBars,
                FlagBars = flagBars,
                PullbackDepthR = pullbackDepthR,
                CompressionScore = compressionScore,
                QualityScore = qualityScore,
                IsValid = isValid,
                BonusScore = bonus,
                Reason = reason
            };
        }

        private static int FindPullbackEnd(EntryContext ctx, int start, int end, TradeDirection impulseDirection, int minBars)
        {
            int bars = 0;
            for (int i = start; i <= end; i++)
            {
                double dClose = ctx.M5.ClosePrices[i] - ctx.M5.ClosePrices[i - 1];
                bool counterMove = impulseDirection == TradeDirection.Long ? dClose <= 0 : dClose >= 0;

                if (counterMove)
                {
                    bars++;
                    continue;
                }

                if (bars >= minBars)
                {
                    return i - 1;
                }

                bars = 0;
            }

            return bars > 0 ? end : -1;
        }

        private static bool ValidateNoStructureBreak(EntryContext ctx, int flagStart, int flagEnd, TradeDirection impulseDirection, int pullbackStart, int pullbackEnd)
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
            {
                low = Math.Min(low, ctx.M5.LowPrices[i]);
            }

            return low;
        }

        private static double MaxHigh(EntryContext ctx, int start, int end)
        {
            double high = double.MinValue;
            for (int i = start; i <= end; i++)
            {
                high = Math.Max(high, ctx.M5.HighPrices[i]);
            }

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

        private static string BuildReason(bool hasImpulse, bool hasPullback, bool hasFlag, bool flagStructureBroken, double pullbackDepthR, double maxPullbackDepthR, double compression, double maxCompression)
        {
            if (!hasImpulse)
                return "MissingImpulse";

            if (!hasPullback)
            {
                if (pullbackDepthR > maxPullbackDepthR || pullbackDepthR > 0.5)
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
