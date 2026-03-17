using System;
using cAlgo.API;
using GeminiV26.Core.Entry;

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
            if (ctx == null || pos == null)
                return false;

            // TP1 után nem zárunk viability alapon
            if (ctx.Tp1Hit)
                return false;

            if (m5 == null || m5.Count < 4)
                return false;

            double risk = ctx.RiskPriceDistance;
            if (risk <= 0)
                return false;

            int currentBarIndex;
            int entryBarIndex;
            int barsSinceEntry = ComputeBarsSinceEntryByIndex(ctx, m5, out currentBarIndex, out entryBarIndex);
            ctx.BarsSinceEntryM5 = barsSinceEntry;

            if (barsSinceEntry <= 12)
            {
                _bot.Print(
                    $"[TVM DBG] barsSinceEntry={barsSinceEntry} currentBarIndex={currentBarIndex} entryBarIndex={entryBarIndex}"
                );
            }

            if (barsSinceEntry <= 0)
                return false;

            UpdateMfeMae(ctx, pos, risk);

            bool marketTrend = ctx.MarketTrend;
            double adxNow = EstimateAdxLikeStrength(m5, 5);
            bool atrShrinking = IsAtrShrinking(m5);

            if (barsSinceEntry <= 3)
            {
                return EvaluateEarlyPhase(ctx, barsSinceEntry, adxNow, atrShrinking, marketTrend);
            }

            if (barsSinceEntry <= 10)
            {
                return EvaluateDevelopmentPhase(ctx, barsSinceEntry, adxNow, marketTrend, m5, pos.TradeType);
            }

            return EvaluateMaturePhase(ctx, barsSinceEntry, adxNow, marketTrend);
        }

        private int ComputeBarsSinceEntryByIndex(
            PositionContext ctx,
            Bars m5,
            out int currentBarIndex,
            out int entryBarIndex)
        {
            currentBarIndex = m5.Count - 1;
            if (currentBarIndex < 0)
            {
                entryBarIndex = 0;
                return 0;
            }

            DateTime entryTime = ctx.EntryTime;
            DateTime firstBarTime = m5.OpenTimes.Last(currentBarIndex);

            if (entryTime <= firstBarTime)
            {
                entryBarIndex = 0;
                return currentBarIndex;
            }

            entryBarIndex = currentBarIndex;
            int offset = 0;
            while (offset <= currentBarIndex)
            {
                DateTime barOpenTime = m5.OpenTimes.Last(offset);
                if (barOpenTime <= entryTime)
                {
                    entryBarIndex = currentBarIndex - offset;
                    break;
                }

                offset++;
            }

            int barsSinceEntry = currentBarIndex - entryBarIndex;
            if (barsSinceEntry < 0)
                barsSinceEntry = 0;

            return barsSinceEntry;
        }

        private void UpdateMfeMae(PositionContext ctx, Position pos, double risk)
        {
            if (ctx.FinalDirection == TradeDirection.None)
            {
                _bot.Print($"[DIR][TVM_CTX_ERROR] Missing FinalDirection posId={ctx.PositionId}");
                return;
            }

            bool isLong = ctx.FinalDirection == TradeDirection.Long;

            double currentPrice =
                isLong
                    ? pos.Symbol.Bid
                    : pos.Symbol.Ask;

            double favorableMove =
                isLong
                    ? currentPrice - ctx.EntryPrice
                    : ctx.EntryPrice - currentPrice;

            double adverseMove =
                isLong
                    ? ctx.EntryPrice - currentPrice
                    : currentPrice - ctx.EntryPrice;

            double favorableR = favorableMove / risk;
            double adverseR = adverseMove / risk;

            if (favorableR > ctx.MfeR)
                ctx.MfeR = favorableR;

            if (adverseR > ctx.MaeR)
                ctx.MaeR = adverseR;
        }

        private bool EvaluateEarlyPhase(
            PositionContext ctx,
            int barsSinceEntry,
            double adxNow,
            bool atrShrinking,
            bool marketTrend)
        {
            _bot.Print(
                $"[TVM PHASE] EARLY bars={barsSinceEntry} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                $"adx={adxNow:0.0} trend={marketTrend}"
            );

            bool noProgress = barsSinceEntry >= 2 && ctx.MfeR < 0.10;
            bool adverseExpansion = ctx.MaeR > 0.35;
            bool momentumWeak = adxNow < 20.0 || atrShrinking;
            bool fastAdverse = ctx.MaeR > 0.25 && barsSinceEntry <= 2;

            _bot.Print(
                $"[TVM EARLY] noProgress={noProgress} adverseExpansion={adverseExpansion} " +
                $"momentumWeak={momentumWeak} atrShrinking={atrShrinking}"
            );
            _bot.Print(
                $"[TVM EARLY] fastAdverse={fastAdverse} maeR={ctx.MaeR:0.00} bars={barsSinceEntry}"
            );

            int dangerCount = 0;
            if (noProgress)
                dangerCount++;
            if (adverseExpansion)
                dangerCount++;
            if (momentumWeak)
                dangerCount++;
            if (fastAdverse)
                dangerCount++;

            _bot.Print($"[TVM DECISION] phase=EARLY dangerCount={dangerCount} threshold=2");

            if (dangerCount >= 2)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "EARLY_FAILURE";

                _bot.Print(
                    $"[TVM EXIT] reason=EARLY_FAILURE mfeR={ctx.MfeR:0.00} " +
                    $"maeR={ctx.MaeR:0.00} bars={barsSinceEntry}"
                );

                return true;
            }

            return false;
        }

        private bool EvaluateDevelopmentPhase(
            PositionContext ctx,
            int barsSinceEntry,
            double adxNow,
            bool marketTrend,
            Bars m5,
            TradeType tradeType)
        {
            _bot.Print(
                $"[TVM PHASE] DEVELOPMENT bars={barsSinceEntry} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                $"adx={adxNow:0.0} trend={marketTrend}"
            );

            bool momentumDecay = IsMomentumDecaying(m5, 4);
            bool structureBreak = ctx.MaeR > 0.60 || IsStructureWeakening(tradeType, m5);
            bool noContinuation = barsSinceEntry >= 6 && ctx.MfeR < 0.25;

            _bot.Print(
                $"[TVM DEVELOPMENT] momentumDecay={momentumDecay} noContinuation={noContinuation} " +
                $"structureBreak={structureBreak}"
            );

            bool shouldExit = structureBreak || (momentumDecay && noContinuation);

            _bot.Print(
                $"[TVM DECISION] phase=DEVELOPMENT structureBreak={structureBreak} " +
                $"combo={(momentumDecay && noContinuation)}"
            );

            if (shouldExit)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "DEVELOPMENT_FAILURE";

                _bot.Print(
                    $"[TVM EXIT] reason=DEVELOPMENT_FAILURE mfeR={ctx.MfeR:0.00} " +
                    $"maeR={ctx.MaeR:0.00} bars={barsSinceEntry}"
                );

                return true;
            }

            return false;
        }

        private bool EvaluateMaturePhase(
            PositionContext ctx,
            int barsSinceEntry,
            double adxNow,
            bool marketTrend)
        {
            _bot.Print(
                $"[TVM PHASE] MATURE bars={barsSinceEntry} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                $"adx={adxNow:0.0} trend={marketTrend}"
            );

            bool maximumAdverseExcursion = ctx.MaeR > 0.80;
            bool weakDevelopment = barsSinceEntry > 12 && ctx.MfeR < 0.30;

            _bot.Print(
                $"[TVM MATURE] maxAdverse={maximumAdverseExcursion} weakDevelopment={weakDevelopment}"
            );

            bool shouldExit = maximumAdverseExcursion || weakDevelopment;

            _bot.Print(
                $"[TVM DECISION] phase=MATURE maxAdverse={maximumAdverseExcursion} weakDevelopment={weakDevelopment}"
            );

            if (shouldExit)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "MATURE_FAILURE";

                _bot.Print(
                    $"[TVM EXIT] reason=MATURE_FAILURE mfeR={ctx.MfeR:0.00} " +
                    $"maeR={ctx.MaeR:0.00} bars={barsSinceEntry}"
                );

                return true;
            }

            return false;
        }

        private bool IsAtrShrinking(Bars m5)
        {
            if (m5 == null || m5.Count < 7)
                return false;

            double recent =
                (m5.HighPrices.Last(0) - m5.LowPrices.Last(0)) +
                (m5.HighPrices.Last(1) - m5.LowPrices.Last(1)) +
                (m5.HighPrices.Last(2) - m5.LowPrices.Last(2));

            double previous =
                (m5.HighPrices.Last(3) - m5.LowPrices.Last(3)) +
                (m5.HighPrices.Last(4) - m5.LowPrices.Last(4)) +
                (m5.HighPrices.Last(5) - m5.LowPrices.Last(5));

            return recent < previous;
        }

        private bool IsMomentumDecaying(Bars m5, int window)
        {
            if (m5 == null)
                return false;

            int requiredBars = (window * 2) + 2;
            if (m5.Count < requiredBars)
                return false;

            double recentStrength = EstimateDirectionalStrength(m5, 0, window);
            double previousStrength = EstimateDirectionalStrength(m5, window, window);

            return recentStrength < previousStrength;
        }

        private double EstimateAdxLikeStrength(Bars m5, int window)
        {
            if (m5 == null)
                return 0.0;

            if (window < 2)
                window = 2;

            int maxWindow = m5.Count - 1;
            if (maxWindow < 2)
                return 0.0;

            if (window > maxWindow)
                window = maxWindow;

            return EstimateDirectionalStrength(m5, 0, window);
        }

        private double EstimateDirectionalStrength(Bars m5, int startOffset, int window)
        {
            if (m5 == null)
                return 0.0;

            if (window < 2)
                return 0.0;

            int lastNeededOffset = startOffset + window;
            if (m5.Count <= lastNeededOffset)
                return 0.0;

            double netMove = Math.Abs(m5.ClosePrices.Last(startOffset) - m5.ClosePrices.Last(startOffset + window));
            double totalMove = 0.0;

            int i = 0;
            while (i < window)
            {
                double c0 = m5.ClosePrices.Last(startOffset + i);
                double c1 = m5.ClosePrices.Last(startOffset + i + 1);
                totalMove += Math.Abs(c0 - c1);
                i++;
            }

            if (totalMove <= 0.0)
                return 0.0;

            return (netMove / totalMove) * 100.0;
        }

        private bool IsStructureWeakening(TradeType tradeType, Bars m5)
        {
            if (m5 == null || m5.Count < 4)
                return false;

            double c0 = m5.ClosePrices.Last(0);
            double c1 = m5.ClosePrices.Last(1);
            double c2 = m5.ClosePrices.Last(2);

            if (tradeType == TradeType.Buy)
                return c0 < c1 && c1 < c2;

            return c0 > c1 && c1 > c2;
        }
    }
}
