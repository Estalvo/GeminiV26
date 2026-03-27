using System;
using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;

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

            if (ctx.LastTvmEvalBar == currentBarIndex)
                return false;

            ctx.LastTvmEvalBar = currentBarIndex;

            LogTvmOncePerBar(
                ctx,
                currentBarIndex,
                $"[TVM] barsSinceEntry={barsSinceEntry} currentBarIndex={currentBarIndex} entryBarIndex={entryBarIndex}");

            if (barsSinceEntry <= 0)
                return false;

            UpdateMfeMae(ctx, pos, risk);

            bool marketTrend = ctx.MarketTrend;
            bool atrShrinking = IsAtrShrinking(m5);

            if (barsSinceEntry <= 3)
            {
                return EvaluateEarlyPhase(ctx, barsSinceEntry, atrShrinking, marketTrend, m5, pos.TradeType);
            }

            if (barsSinceEntry <= 10)
            {
                return EvaluateDevelopmentPhase(ctx, barsSinceEntry, marketTrend, m5, pos.TradeType);
            }

            return EvaluateMaturePhase(ctx, barsSinceEntry, marketTrend);
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
                if (!ctx.MissingDirLogged)
                {
                    _bot.Print($"[DIR][ERROR] Missing FinalDirection posId={ctx.PositionId}");
                    ctx.MissingDirLogged = true;
                }

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
            bool atrShrinking,
            bool marketTrend,
            Bars m5,
            TradeType tradeType)
        {
            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM PHASE] EARLY bars={barsSinceEntry} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                $"adx={ctx.Adx_M5:0.0} trend={marketTrend}");

            // =====================================
            // HARD EARLY PROTECTION (CRITICAL FIX)
            // =====================================
            if (barsSinceEntry <= 2)
            {
                // csak brutál fail esetén engedünk exitet
                bool hardFail = ctx.MaeR > 0.60;

                _bot.Print(TradeLogIdentity.WithPositionIds(
                    $"[TVM][EARLY_PROTECT] bars={barsSinceEntry} maeR={ctx.MaeR:0.00} hardFail={hardFail}", ctx));

                if (!hardFail)
                {
                    _bot.Print(TradeLogIdentity.WithPositionIds(
                        "[TVM][HOLD][EARLY_PROTECTION_ACTIVE]", ctx));

                    return false;
                }

                _bot.Print(TradeLogIdentity.WithPositionIds(
                    "[TVM][ALLOW_EXIT][EARLY_HARD_FAIL]", ctx));
            }

            if (!ctx.MarketTrend)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "TREND_LOST";
                return true;
            }

            bool noFollowThrough =
                barsSinceEntry >= 4 &&
                ctx.MfeR < 0.15 &&
                ctx.MaeR > 0.25;

            if (noFollowThrough)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "NO_FOLLOW_THROUGH";
                return true;
            }

            bool noProgress = barsSinceEntry >= 3 && ctx.MfeR < 0.10;
            bool adverseExpansion = ctx.MaeR > 0.35;
            bool momentumWeak = ctx.Adx_M5 < 20.0 || atrShrinking;
            bool fastAdverse = ctx.MaeR > 0.35 && barsSinceEntry <= 2;

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM EARLY] noProgress={noProgress} adverseExpansion={adverseExpansion} " +
                $"momentumWeak={momentumWeak} atrShrinking={atrShrinking} fastAdverse={fastAdverse} " +
                $"maeR={ctx.MaeR:0.00} bars={barsSinceEntry}");

            int dangerCount = 0;
            if (noProgress)
                dangerCount++;
            if (adverseExpansion)
                dangerCount++;
            if (momentumWeak)
                dangerCount++;
            if (fastAdverse)
                dangerCount++;

            LogTvmOncePerBar(ctx, ctx.LastTvmEvalBar, $"[TVM DECISION] phase=EARLY dangerCount={dangerCount} threshold=2");

            if (dangerCount >= 2)
            {
                bool persistenceAlive = TrendPersistenceAlive(ctx, m5, tradeType);
                _bot.Print(TradeLogIdentity.WithPositionIds($"[TVM][PERSISTENCE] alive={persistenceAlive}", ctx));
                if (persistenceAlive)
                {
                    _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][BLOCK_EXIT] reason=TREND_PERSISTENCE_ALIVE", ctx));
                    return false;
                }

                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "EARLY_FAILURE";
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][ALLOW_EXIT] reason=EARLY_FAILURE", ctx));

                LogTvmOncePerBar(
                    ctx,
                    ctx.LastTvmEvalBar,
                    $"[TVM EXIT] reason=EARLY_FAILURE mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} bars={barsSinceEntry}");

                return true;
            }

            return false;
        }

        private bool EvaluateDevelopmentPhase(
            PositionContext ctx,
            int barsSinceEntry,
            bool marketTrend,
            Bars m5,
            TradeType tradeType)
        {
            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM PHASE] DEVELOPMENT bars={barsSinceEntry} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                $"adx={ctx.Adx_M5:0.0} trend={marketTrend}");

            if (!ctx.MarketTrend)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "TREND_LOST";
                return true;
            }

            bool noFollowThrough =
                barsSinceEntry >= 3 &&
                ctx.MfeR < 0.15 &&
                ctx.MaeR > 0.25;

            if (noFollowThrough)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "NO_FOLLOW_THROUGH";
                return true;
            }

            if (barsSinceEntry >= 4 && ctx.MfeR <= 0.05)
            {
                bool persistenceAlive = TrendPersistenceAlive(ctx, m5, tradeType);
                _bot.Print(TradeLogIdentity.WithPositionIds($"[TVM][PERSISTENCE] alive={persistenceAlive}", ctx));
                if (persistenceAlive)
                {
                    _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][BLOCK_EXIT] reason=TREND_PERSISTENCE_ALIVE", ctx));
                    return false;
                }

                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "NO_PROGRESS";
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][ALLOW_EXIT] reason=NO_PROGRESS", ctx));
                return true;
            }

            bool momentumDecay = IsMomentumDecaying(m5, 4);
            bool structureBreak = ctx.MaeR > 0.60 || IsStructureWeakening(tradeType, m5);
            bool noContinuation = barsSinceEntry >= 4 && ctx.MfeR < 0.20;

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM DEVELOPMENT] momentumDecay={momentumDecay} noContinuation={noContinuation} structureBreak={structureBreak}");

            bool shouldExit = structureBreak || (momentumDecay && noContinuation);

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM DECISION] phase=DEVELOPMENT structureBreak={structureBreak} combo={(momentumDecay && noContinuation)}");

            if (shouldExit)
            {
                bool persistenceAlive = TrendPersistenceAlive(ctx, m5, tradeType);
                _bot.Print(TradeLogIdentity.WithPositionIds($"[TVM][PERSISTENCE] alive={persistenceAlive}", ctx));
                if (persistenceAlive && !structureBreak)
                {
                    _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][BLOCK_EXIT] reason=TREND_PERSISTENCE_ALIVE", ctx));
                    return false;
                }

                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "DEVELOPMENT_FAILURE";
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][ALLOW_EXIT] reason=DEVELOPMENT_FAILURE", ctx));

                LogTvmOncePerBar(
                    ctx,
                    ctx.LastTvmEvalBar,
                    $"[TVM EXIT] reason=DEVELOPMENT_FAILURE mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} bars={barsSinceEntry}");

                return true;
            }

            return false;
        }

        private bool EvaluateMaturePhase(
            PositionContext ctx,
            int barsSinceEntry,
            bool marketTrend)
        {
            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM PHASE] MATURE bars={barsSinceEntry} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} " +
                $"adx={ctx.Adx_M5:0.0} trend={marketTrend}");

            if (!ctx.MarketTrend)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "TREND_LOST";
                return true;
            }

            bool maximumAdverseExcursion = ctx.MaeR > 0.80;
            bool weakDevelopment = barsSinceEntry > 12 && ctx.MfeR < 0.30;

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM MATURE] maxAdverse={maximumAdverseExcursion} weakDevelopment={weakDevelopment}");

            bool shouldExit = maximumAdverseExcursion || weakDevelopment;

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM DECISION] phase=MATURE maxAdverse={maximumAdverseExcursion} weakDevelopment={weakDevelopment}");

            if (shouldExit)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "MATURE_FAILURE";

                LogTvmOncePerBar(
                    ctx,
                    ctx.LastTvmEvalBar,
                    $"[TVM EXIT] reason=MATURE_FAILURE mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} bars={barsSinceEntry}");

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

        private bool TrendPersistenceAlive(PositionContext ctx, Bars m5, TradeType tradeType)
        {
            if (ctx == null || m5 == null || m5.Count < 4)
                return false;

            if (!ctx.MarketTrend)
                return false;

            bool adxHealthy = ctx.Adx_M5 >= 20.0;
            bool volatilitySupport = ctx.MfeR >= 0.20 || ctx.MaeR <= 0.35;
            bool structureIntact = !IsStructureWeakening(tradeType, m5);
            bool adxStrong = ctx.Adx_M5 >= 35.0;
            bool earlyWindow = ctx.BarsSinceEntryM5 <= 4;
            if (!structureIntact && adxStrong && earlyWindow && ctx.MaeR <= 0.20)
                structureIntact = true;

            return adxHealthy && volatilitySupport && structureIntact;
        }

        private void LogTvmOncePerBar(PositionContext ctx, int barIndex, string message)
        {
            if (ctx == null)
                return;

            if (ctx.LastTvmLogBar == barIndex)
                return;

            _bot.Print(TradeLogIdentity.WithPositionIds(message, ctx));
            ctx.LastTvmLogBar = barIndex;
        }
    }
}
