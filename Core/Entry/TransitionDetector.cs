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

            for (int i = last; i >= Math.Max(1, last - rules.MaxImpulseAge); i--)
            {
                double range = ctx.M5.HighPrices[i] - ctx.M5.LowPrices[i];
                double body = Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]);
                double bodyRatio = range > 0 ? body / range : 0.0;
                bool directional = Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]) > 1e-12;

                if (directional &&
                    range > ctx.AtrM5 * rules.ImpulseMultiplier &&
                    bodyRatio >= rules.MinImpulseBodyRatio)
                {
                    impulseIndex = i;
                    impulseDirection = ctx.M5.ClosePrices[i] > ctx.M5.OpenPrices[i] ? TradeDirection.Long : TradeDirection.Short;
                    impulseRange = range;
                    break;
                }
            }

            bool hasImpulse = impulseIndex >= 0;
            int barsSinceImpulse = hasImpulse ? last - impulseIndex : int.MaxValue;
            if (hasImpulse && barsSinceImpulse > rules.MaxImpulseAge)
            {
                hasImpulse = false;
            }

            double strength = hasImpulse && ctx.AtrM5 > 0 ? impulseRange / ctx.AtrM5 : 0.0;
            ctx.Log?.Invoke($"[TRANSITION][IMPULSE] detected={hasImpulse.ToString().ToLowerInvariant()} barsSince={barsSinceImpulse} strength={strength:0.00}");

            int pullbackStart = hasImpulse ? impulseIndex + 1 : -1;
            int pullbackEnd = -1;
            int pullbackBars = 0;
            double pullbackDepthR = 0.0;
            bool trendAlignmentMaintained = false;
            bool hasPullback = false;

            if (hasImpulse && pullbackStart <= last)
            {
                pullbackEnd = FindPullbackEnd(ctx, pullbackStart, last, impulseDirection, rules.MinPullbackBars);
                pullbackBars = pullbackEnd >= pullbackStart ? (pullbackEnd - pullbackStart + 1) : 0;

                if (pullbackBars > 0 && impulseRange > 0)
                {
                    double impulseClose = ctx.M5.ClosePrices[impulseIndex];
                    if (impulseDirection == TradeDirection.Long)
                    {
                        double low = MinLow(ctx, pullbackStart, pullbackEnd);
                        pullbackDepthR = Math.Max(0.0, impulseClose - low) / impulseRange;
                        trendAlignmentMaintained = low > ctx.M5.LowPrices[impulseIndex];
                    }
                    else
                    {
                        double high = MaxHigh(ctx, pullbackStart, pullbackEnd);
                        pullbackDepthR = Math.Max(0.0, high - impulseClose) / impulseRange;
                        trendAlignmentMaintained = high < ctx.M5.HighPrices[impulseIndex];
                    }
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
            bool noStructureBreak = false;
            bool hasFlag = false;

            if (hasImpulse && hasPullback && flagBars > 0)
            {
                double avgRange = AverageRange(ctx, flagStart, last);
                compression = impulseRange > 0 ? avgRange / impulseRange : 1.0;
                noStructureBreak = ValidateNoStructureBreak(ctx, flagStart, last, impulseDirection, pullbackStart, pullbackEnd);

                hasFlag = flagBars <= rules.MaxFlagBars
                    && compression <= rules.MaxCompressionRatio
                    && noStructureBreak;
            }

            ctx.Log?.Invoke($"[TRANSITION][FLAG] bars={flagBars} compression={compression:0.00}");

            bool isValid = hasImpulse && hasPullback && hasFlag;
            int bonus = isValid ? 10 : 0;
            string reason = isValid ? "OK" : BuildReason(hasImpulse, hasPullback, hasFlag, pullbackDepthR, rules.MaxPullbackDepthR, compression, rules.MaxCompressionRatio);

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
                CompressionScore = compression,
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

        private static string BuildReason(bool hasImpulse, bool hasPullback, bool hasFlag, double pullbackDepthR, double maxPullbackDepthR, double compression, double maxCompression)
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
                if (compression > maxCompression)
                    return "WeakCompression";

                return "InvalidFlag";
            }

            return "Unknown";
        }
    }
}
