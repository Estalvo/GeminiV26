using System;
using cAlgo.API;

namespace GeminiV26.Core
{
    public class TradeViabilityMonitor
    {
        private readonly Robot _bot;

        public TradeViabilityMonitor(Robot bot)
        {
            _bot = bot;
        }

        public bool ShouldEarlyExit(
            PositionContext ctx,
            Position pos,
            Bars m5,
            Bars m15)
        {
            // 🔒 Hard guard
            if (ctx.Tp1Hit)
                return false;

            int signals = 0;

            // ① M5 STRUCTURE BREAK
            if (IsM5StructureBroken(pos, m5))
                signals++;

            // ② M15 BIAS LOST
            if (IsM15BiasLost(pos, m15))
                signals++;

            // ③ MOMENTUM FADE
            if (IsMomentumFading(m5))
                signals++;

            // ④ FX NO FOLLOW-THROUGH (FIXED)
            if (IsFxNoFollowThrough(ctx, pos))
                signals++;

            return signals >= 2;
        }

        // ---------------------------------

        private bool IsFxNoFollowThrough(
            PositionContext ctx,
            Position pos)
        {
            // FX only
            if (!IsFxSymbol(ctx.Symbol))
                return false;

            // minimum idő: ~4 M5 bar
            if ((DateTime.UtcNow - ctx.EntryTime).TotalMinutes < 20)
                return false;

            // progress price-ban
            double progress =
                Math.Abs(pos.Pips * pos.Symbol.PipSize);

            // risk distance = entrykori SL távolság
            double risk = ctx.RiskPriceDistance;

            if (risk <= 0)
                return false;

            // < 0.25R előrehaladás
            bool lowProgress = progress < risk * 0.25;

            // ár vissza entry közelébe
            bool backToEntry =
                Math.Abs(pos.Symbol.Bid - ctx.EntryPrice) < risk * 0.2;

            return lowProgress && backToEntry;
        }

        private bool IsFxSymbol(string symbol)
        {
            return symbol.StartsWith("EUR")
                || symbol.StartsWith("GBP")
                || symbol.StartsWith("USD")
                || symbol.StartsWith("AUD")
                || symbol.StartsWith("NZD");
        }

        // ----- meglévő metódusok változatlanul -----

        private bool IsM5StructureBroken(Position pos, Bars m5)
        {
            if (m5.Count < 3)
                return false;

            double c0 = m5.ClosePrices.Last(0);
            double c1 = m5.ClosePrices.Last(1);
            double c2 = m5.ClosePrices.Last(2);

            return pos.TradeType == TradeType.Buy
                ? c0 < c1 && c1 < c2
                : c0 > c1 && c1 > c2;
        }

        private bool IsM15BiasLost(Position pos, Bars m15)
        {
            if (m15.Count < 2)
                return false;

            var ema21 = _bot.Indicators
                .ExponentialMovingAverage(m15.ClosePrices, 21);

            double lastClose = m15.ClosePrices.Last(0);
            double emaValue = ema21.Result.LastValue;

            return pos.TradeType == TradeType.Buy
                ? lastClose < emaValue
                : lastClose > emaValue;
        }

        private bool IsMomentumFading(Bars m5)
        {
            if (m5.Count < 4)
                return false;

            double r0 = m5.HighPrices.Last(0) - m5.LowPrices.Last(0);
            double r1 = m5.HighPrices.Last(1) - m5.LowPrices.Last(1);
            double r2 = m5.HighPrices.Last(2) - m5.LowPrices.Last(2);

            return r0 < r1 && r1 < r2;
        }
    }
}
