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
            bool hasImpulse = ctx.HasImpulse_M5 && impulseBarsAgo <= p.MaxImpulseBars;

            TradeDirection direction = ctx.TrendDirection;
            if (direction == TradeDirection.None)
                direction = InferDirection(ctx, last);

            double impulseStrength = GetImpulseStrength(ctx, last, impulseBarsAgo);
            _log?.Invoke($"[TRANSITION][IMPULSE] detected={hasImpulse} barsSince={impulseBarsAgo} strength={impulseStrength:0.00}");

            int pullbackBars = Math.Max(0, ctx.PullbackBars_M5);
            double pullbackDepthR = Math.Max(0.0, ctx.PullbackDepthAtr_M5 / 2.0);
            bool hasPullback = pullbackBars > 0 && pullbackDepthR <= p.MaxPullbackDepthR;

            if (hasPullback && p.StrictWickFilter)
            {
                hasPullback = ctx.HasRejectionWick_M5;
            }

            _log?.Invoke($"[TRANSITION][PULLBACK] depthR={pullbackDepthR:0.00} bars={pullbackBars}");

            int flagBars = EstimateFlagBars(ctx, last, p.MaxFlagBars, direction);
            double compressionScore = ComputeCompressionScore(ctx, last, flagBars);
            bool hasFlag = flagBars > 0 && flagBars <= p.MaxFlagBars && compressionScore >= p.MinCompressionScore;

            _log?.Invoke($"[TRANSITION][FLAG] bars={flagBars} compression={compressionScore:0.00}");

            bool valid = hasImpulse && hasPullback && hasFlag;
            int bonus = valid ? 10 : 0;

            string reason = "OK";
            if (!hasImpulse) reason = "NoImpulse";
            else if (!hasPullback) reason = pullbackDepthR > p.MaxPullbackDepthR ? "PullbackTooDeep" : "NoPullback";
            else if (!hasFlag) reason = "NoFlagCompression";

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

        private static double ComputeCompressionScore(EntryContext ctx, int last, int flagBars)
        {
            if (flagBars <= 1)
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
            if (range <= 0 || ctx.AtrM5 <= 0)
                return 0.0;

            double rangeCompression = 1.0 - Math.Min(1.0, range / (ctx.AtrM5 * 2.0));
            double bodyCompression = 1.0 - Math.Min(1.0, avgBody / (ctx.AtrM5 * 0.9));

            return Math.Max(0.0, Math.Min(1.0, (rangeCompression * 0.6) + (bodyCompression * 0.4)));
        }
    }
}
