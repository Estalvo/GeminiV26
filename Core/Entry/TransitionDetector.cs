using System;

namespace GeminiV26.Core.Entry
{
    public sealed class TransitionDetector
    {
        private readonly Action<string> _log;

        public TransitionDetector(Action<string> log)
        {
            _log = log;
        }

        public TransitionEvaluation Evaluate(EntryContext ctx)
        {
            if (ctx == null || ctx.M5 == null || ctx.M5.Count < 25 || ctx.AtrM5 <= 0)
            {
                return new TransitionEvaluation
                {
                    Direction = "None",
                    Reason = "InsufficientData"
                };
            }

            var p = TransitionParams.ForSymbol(ctx.Symbol);
            int last = ctx.M5.Count - 2;

            int impulseBarsAgo = Math.Max(0, ctx.BarsSinceImpulse_M5);
            bool impulseTooOld = ctx.HasImpulse_M5 && impulseBarsAgo > p.MaxImpulseBars;
            bool hasImpulse = ctx.HasImpulse_M5 && !impulseTooOld;
            int impulseIndex = Math.Max(1, last - impulseBarsAgo);

            TradeDirection direction = ctx.TrendDirection;
            if (direction == TradeDirection.None)
                direction = InferDirection(ctx, last);

            TradeDirection impulseDirection = DetectImpulseDirection(ctx, impulseIndex, direction);
            bool impulseDirectionValid = impulseDirection != TradeDirection.None && impulseDirection == direction;

            double impulseStrength = GetImpulseStrength(ctx, last, impulseBarsAgo);
            _log?.Invoke($"[TRANSITION][IMPULSE] detected={hasImpulse} barsSince={impulseBarsAgo} strength={impulseStrength:0.00} direction={impulseDirection}");

            int pullbackBars = Math.Max(0, ctx.PullbackBars_M5);
            TradeDirection pullbackDirection = DetectPullbackDirection(ctx, last, pullbackBars);
            bool pullbackDirectionValid = pullbackDirection == OppositeOf(impulseDirection);

            double impulseRange = GetImpulseRange(ctx, impulseIndex);
            double pullbackDepthR = ComputePullbackDepthR(ctx, last, pullbackBars, impulseDirection, impulseRange);
            bool hasPullback = pullbackBars > 0 && pullbackDepthR <= p.MaxPullbackDepthR && pullbackDirectionValid;

            if (hasPullback && p.StrictWickFilter)
            {
                hasPullback = ctx.HasRejectionWick_M5;
            }

            _log?.Invoke($"[TRANSITION][PULLBACK] depthR={pullbackDepthR:0.00} bars={pullbackBars} direction={pullbackDirection}");

            int flagBars = EstimateFlagBars(ctx, last, p.MaxFlagBars, direction);
            double compressionScore = ComputeCompressionScore(ctx, last, flagBars, impulseRange);
            bool flagDirectionValid = IsFlagDirectionValid(ctx, last, flagBars, impulseDirection, ctx.AtrM5);
            bool hasFlag =
                flagBars > 0 &&
                flagBars <= p.MaxFlagBars &&
                compressionScore >= p.MinCompressionScore &&
                flagDirectionValid;

            _log?.Invoke($"[TRANSITION][FLAG] bars={flagBars} compression={compressionScore:0.00} directionValid={flagDirectionValid}");

            bool valid = hasImpulse && impulseDirectionValid && hasPullback && hasFlag;
            int bonus = valid ? 10 : 0;

            string reason = "OK";
            if (!hasImpulse) reason = impulseTooOld ? "ImpulseTooOld" : "NoImpulse";
            else if (!impulseDirectionValid) reason = "InvalidImpulseDirection";
            else if (!hasPullback)
            {
                if (!pullbackDirectionValid) reason = "InvalidPullbackDirection";
                else reason = pullbackDepthR > p.MaxPullbackDepthR ? "PullbackTooDeep" : "NoPullback";
            }
            else if (!hasFlag) reason = !flagDirectionValid ? "InvalidFlagDirection" : "CompressionTooLow";

            _log?.Invoke($"[TRANSITION][DECISION] valid={valid} bonus={bonus} reason={reason}");

            return new TransitionEvaluation
            {
                HasImpulse = hasImpulse,
                HasPullback = hasPullback,
                HasFlag = hasFlag,
                BarsSinceImpulse = impulseBarsAgo,
                PullbackBars = pullbackBars,
                PullbackDepthR = pullbackDepthR,
                FlagBars = flagBars,
                CompressionScore = compressionScore,
                TransitionValid = valid,
                BonusScore = bonus,
                Direction = direction.ToString(),
                Reason = reason
            };
        }

        private static TradeDirection InferDirection(EntryContext ctx, int last)
        {
            return ctx.M5.ClosePrices[last] >= ctx.Ema21_M5
                ? TradeDirection.Long
                : TradeDirection.Short;
        }

        private static double GetImpulseStrength(EntryContext ctx, int last, int barsAgo)
        {
            int impulseIndex = Math.Max(0, last - barsAgo);
            double body = Math.Abs(ctx.M5.ClosePrices[impulseIndex] - ctx.M5.OpenPrices[impulseIndex]);
            return ctx.AtrM5 > 0 ? body / ctx.AtrM5 : 0.0;
        }

        private static TradeDirection DetectImpulseDirection(EntryContext ctx, int impulseIndex, TradeDirection fallback)
        {
            double impulseMove = ctx.M5.ClosePrices[impulseIndex] - ctx.M5.OpenPrices[impulseIndex];
            if (Math.Abs(impulseMove) < 1e-12)
                return fallback;

            return impulseMove > 0 ? TradeDirection.Long : TradeDirection.Short;
        }

        private static TradeDirection DetectPullbackDirection(EntryContext ctx, int last, int pullbackBars)
        {
            if (pullbackBars <= 0)
                return TradeDirection.None;

            int start = Math.Max(1, last - pullbackBars + 1);
            double move = ctx.M5.ClosePrices[last] - ctx.M5.ClosePrices[start - 1];

            if (Math.Abs(move) < 1e-12)
                return TradeDirection.None;

            return move > 0 ? TradeDirection.Long : TradeDirection.Short;
        }

        private static TradeDirection OppositeOf(TradeDirection direction)
        {
            return direction == TradeDirection.Long
                ? TradeDirection.Short
                : direction == TradeDirection.Short ? TradeDirection.Long : TradeDirection.None;
        }

        private static double GetImpulseRange(EntryContext ctx, int impulseIndex)
        {
            double high = ctx.M5.HighPrices[impulseIndex];
            double low = ctx.M5.LowPrices[impulseIndex];
            return Math.Max(0.0, high - low);
        }

        private static double ComputePullbackDepthR(EntryContext ctx, int last, int pullbackBars, TradeDirection impulseDirection, double impulseRange)
        {
            if (pullbackBars <= 0 || impulseRange <= 0)
                return 0.0;

            int start = Math.Max(0, last - pullbackBars + 1);
            double pullbackExtreme = impulseDirection == TradeDirection.Long
                ? double.MaxValue
                : double.MinValue;

            for (int i = start; i <= last; i++)
            {
                if (impulseDirection == TradeDirection.Long)
                    pullbackExtreme = Math.Min(pullbackExtreme, ctx.M5.LowPrices[i]);
                else
                    pullbackExtreme = Math.Max(pullbackExtreme, ctx.M5.HighPrices[i]);
            }

            double impulseAnchor = ctx.M5.ClosePrices[Math.Max(0, start - 1)];
            double retraceSize = impulseDirection == TradeDirection.Long
                ? Math.Max(0.0, impulseAnchor - pullbackExtreme)
                : Math.Max(0.0, pullbackExtreme - impulseAnchor);

            return retraceSize / impulseRange;
        }

        private static bool IsFlagDirectionValid(EntryContext ctx, int last, int flagBars, TradeDirection impulseDirection, double atr)
        {
            if (flagBars <= 1)
                return false;

            int start = Math.Max(0, last - flagBars + 1);
            double move = ctx.M5.ClosePrices[last] - ctx.M5.ClosePrices[start];
            double tolerance = Math.Max(atr * 0.10, 1e-12);

            if (Math.Abs(move) <= tolerance)
                return true;

            TradeDirection flagSlopeDirection = move > 0 ? TradeDirection.Long : TradeDirection.Short;
            return flagSlopeDirection != impulseDirection;
        }

        private static int EstimateFlagBars(EntryContext ctx, int last, int maxFlagBars, TradeDirection direction)
        {
            int bars = 0;
            for (int i = last; i >= Math.Max(0, last - maxFlagBars + 1); i--)
            {
                bool overlap = i > 0 &&
                    ctx.M5.HighPrices[i] <= ctx.M5.HighPrices[i - 1] + (ctx.AtrM5 * 0.25) &&
                    ctx.M5.LowPrices[i] >= ctx.M5.LowPrices[i - 1] - (ctx.AtrM5 * 0.25);

                bool counterSlope = direction == TradeDirection.Long
                    ? ctx.M5.ClosePrices[i] <= ctx.M5.ClosePrices[Math.Max(0, i - 1)] + (ctx.AtrM5 * 0.10)
                    : ctx.M5.ClosePrices[i] >= ctx.M5.ClosePrices[Math.Max(0, i - 1)] - (ctx.AtrM5 * 0.10);

                if (!overlap || !counterSlope)
                    break;

                bars++;
            }

            return bars;
        }

        private static double ComputeCompressionScore(EntryContext ctx, int last, int flagBars, double impulseRange)
        {
            if (flagBars <= 1 || impulseRange <= 0)
                return 0.0;

            int start = Math.Max(0, last - flagBars + 1);
            double hi = double.MinValue;
            double lo = double.MaxValue;
            double avgBody = 0.0;

            for (int i = start; i <= last; i++)
            {
                hi = Math.Max(hi, ctx.M5.HighPrices[i]);
                lo = Math.Min(lo, ctx.M5.LowPrices[i]);
                avgBody += Math.Abs(ctx.M5.ClosePrices[i] - ctx.M5.OpenPrices[i]);
            }

            avgBody /= flagBars;
            double range = hi - lo;
            if (range <= 0)
                return 0.0;

            double rangeCompression = 1.0 - Math.Min(1.0, range / impulseRange);
            double bodyCompression = 1.0;
            if (ctx.AtrM5 > 0)
                bodyCompression = 1.0 - Math.Min(1.0, avgBody / ctx.AtrM5);

            return Math.Max(0.0, Math.Min(1.0, (rangeCompression * 0.6) + (bodyCompression * 0.4)));
        }
    }
}
