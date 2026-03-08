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
            // TP1 után nem zárunk
            if (ctx.Tp1Hit)
                return false;

            if (m5 == null || m5.Count < 6)
                return false;

            double risk = ctx.RiskPriceDistance;
            if (risk <= 0)
                return false;

            string sym = pos.SymbolName ?? string.Empty;

            bool isFx =
                sym == "EURUSD" || sym == "GBPUSD" || sym == "USDJPY" ||
                sym == "AUDNZD" || sym == "AUDUSD" || sym == "NZDUSD" ||
                sym == "USDCHF" || sym == "USDCAD" || sym == "EURJPY" || sym == "GBPJPY";

            bool isCrypto =
                sym == "BTCUSD" || sym == "ETHUSD" || sym == "BTCUSDT" || sym == "ETHUSDT";

            bool isMetal =
                sym == "XAUUSD" || sym == "XAGUSD" || sym == "XPTUSD" || sym == "XPDUSD";

            bool isIndex =
                sym == "NAS100" || sym == "US30" || sym == "SPX500" ||
                sym == "DE40" || sym == "UK100" || sym == "JP225";

            string asset =
                isFx ? "FX" :
                isCrypto ? "CRYPTO" :
                isMetal ? "METAL" :
                isIndex ? "INDEX" :
                "UNKNOWN";

            // =====================================================
            // ASSET THRESHOLDS
            // =====================================================

            // =====================================================
            // SIMPLE TREND DETECTION (M15)
            // =====================================================

            bool marketTrending = false;

            if (m15 != null && m15.Count > 20)
            {
                double move =
                    Math.Abs(
                        m15.ClosePrices.Last(0) -
                        m15.ClosePrices.Last(10)
                    );

                marketTrending = move > pos.Symbol.PipSize * 40;
            }

            double mfeNoProgressR =
                isFx ? 0.12 :
                isCrypto ? 0.30 :
                isIndex ? 0.25 :
                isMetal ? 0.20 :
                0.20;

            double maeAdverseR =
                isFx ? 0.40 :
                isCrypto ? (marketTrending ? 0.35 : 0.25) :
                isIndex ? (marketTrending ? 0.32 : 0.30) :
                isMetal ? (marketTrending ? 0.33 : 0.30) :
                0.35;

            double minMinutesOpen =
                isFx ? 35 :
                isCrypto ? 10 :
                isIndex ? 12 :
                isMetal ? 12 :
                15;

            int dangerThreshold =
                isFx ? 4 :
                isCrypto ? 2 :
                isIndex ? 3 :
                isMetal ? 3 :
                3;

            // =====================================================
            // PRICE + MFE/MAE UPDATE
            // =====================================================

            double currentPrice =
                pos.TradeType == TradeType.Buy
                    ? pos.Symbol.Bid
                    : pos.Symbol.Ask;

            double favorableMove =
                pos.TradeType == TradeType.Buy
                    ? currentPrice - ctx.EntryPrice
                    : ctx.EntryPrice - currentPrice;

            double adverseMove =
                pos.TradeType == TradeType.Buy
                    ? ctx.EntryPrice - currentPrice
                    : currentPrice - ctx.EntryPrice;

            double favorableR = favorableMove / risk;
            double adverseR = adverseMove / risk;

            ctx.MfeR = Math.Max(ctx.MfeR, favorableR);
            ctx.MaeR = Math.Max(ctx.MaeR, adverseR);

            double minutesOpen = (_bot.Server.Time - ctx.EntryTime).TotalMinutes;

            bool noProgress = ctx.MfeR < mfeNoProgressR;
            bool adverseGrowing = ctx.MaeR > maeAdverseR;

            bool structureBroken = IsStructureWeakening(pos, m5);
            bool momentumFade = IsMomentumFading(m5);

            // =====================================================
            // DEBUG SNAP
            // =====================================================

            if (ctx.BarsSinceEntryM5 <= 6)
            {
                _bot.Print(
                    $"[TVM SNAP {asset}] " +
                    $"bars={ctx.BarsSinceEntryM5} " +
                    $"min={minutesOpen:0.0} " +
                    $"trend={marketTrending} " +
                    $"mfeR={ctx.MfeR:0.00} " +
                    $"maeR={ctx.MaeR:0.00} " +
                    $"thrMAE={maeAdverseR:0.00} " +
                    $"noProg={noProgress} " +
                    $"advGrow={adverseGrowing} " +
                    $"structWeak={structureBroken} " +
                    $"momFade={momentumFade}"
                );
            }

            // =====================================================
            // CRYPTO / INDEX FAST DEAD
            // =====================================================

            bool enoughTime = minutesOpen >= minMinutesOpen;
            bool enoughBars = ctx.BarsSinceEntryM5 >= 4;

            if ((isCrypto || isIndex) && enoughTime && enoughBars && !marketTrending)
            {
                bool fastDead =
                    noProgress &&
                    (ctx.MaeR > (maeAdverseR * 0.90));

                if (fastDead)
                {
                    ctx.IsDeadTrade = true;
                    ctx.DeadTradeReason = $"{asset}_FAST_DEAD_NO_IMPULSE";

                    _bot.Print(
                        $"[TVM {asset}] EARLY EXIT | reason={ctx.DeadTradeReason} " +
                        $"mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                        $"barsM5={ctx.BarsSinceEntryM5}"
                    );

                    return true;
                }
            }

            // =====================================================
            // DANGER MATRIX
            // =====================================================

            int danger = 0;

            if (noProgress && enoughTime) danger++;
            if (adverseGrowing && enoughTime) danger++;

            if (!marketTrending && structureBroken) danger++;
            if (!marketTrending && momentumFade) danger++;

            bool exit = danger >= dangerThreshold;

            if (exit)
            {
                ctx.DeadTradeReason = $"{asset}_DANGER";

                _bot.Print(
                    $"[TVM {asset}] THRESHOLD EXIT | " +
                    $"danger={danger}/{dangerThreshold} " +
                    $"mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00}"
                );
            }

            return exit;
        }

        private bool IsStructureWeakening(Position pos, Bars m5)
        {
            if (m5.Count < 4)
                return false;

            double c0 = m5.ClosePrices.Last(0);
            double c1 = m5.ClosePrices.Last(1);
            double c2 = m5.ClosePrices.Last(2);

            if (pos.TradeType == TradeType.Buy)
                return c0 < c1 && c1 < c2;

            return c0 > c1 && c1 > c2;
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