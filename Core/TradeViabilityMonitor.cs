using System;
using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;

namespace GeminiV26.Core
{
    public class TradeViabilityMonitor
    {
        private readonly Robot _bot;
        public int TVM_MinBarsBeforeEvaluation { get; set; } = 4;
        public double TVM_MinAdverseMoveR { get; set; } = 0.20;
        public int TVM_RecoveryLookbackBars { get; set; } = 2;

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

            double unrealizedR = ComputeUnrealizedR(pos, risk);
            string momentumState = IsMomentumDecaying(m5, 4) ? "DECAYING" : "STABLE";
            bool structureBreakDetected = IsStructuredBreak(pos.TradeType, m5, ctx);
            bool strongOppositeImpulseDetected = IsStrongOppositeImpulse(pos.TradeType, m5);
            bool strongHtfConflictDetected = IsStrongHtfConflict(pos.TradeType, m15);
            bool noRecoveryInWindow = !RecentRecoveryDetected(pos.TradeType, m5, TVM_RecoveryLookbackBars);
            bool stillValid =
                !structureBreakDetected &&
                !strongOppositeImpulseDetected &&
                !strongHtfConflictDetected;
            bool slowSetup = IsSlowDevelopmentSetup(ctx?.EntryType);
            bool breakoutSetup = IsBreakoutSetup(ctx?.EntryType);

            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[TVM][EVAL] BarsSinceEntry={barsSinceEntry} UnrealizedR={unrealizedR:0.00} MomentumState={momentumState} " +
                $"StructureState={(structureBreakDetected ? "BROKEN" : "INTACT")} OppImpulse={(strongOppositeImpulseDetected ? "YES" : "NO")} " +
                $"HtfState={(strongHtfConflictDetected ? "STRONG_CONFLICT" : "ALIGNED")} SetupType={ctx?.EntryType}", ctx));

            if (barsSinceEntry < TVM_MinBarsBeforeEvaluation)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    $"[TVM][SKIP_REASON] GRACE barsSinceEntry={barsSinceEntry} minBars={TVM_MinBarsBeforeEvaluation}", ctx));
                return false;
            }

            if (Math.Abs(unrealizedR) < 0.30)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    $"[TVM][SKIP_REASON] NO_MOVE_ZONE unrealizedR={unrealizedR:0.00} threshold=0.30", ctx));
                return false;
            }

            if (unrealizedR > -TVM_MinAdverseMoveR)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    $"[TVM][SKIP_REASON] NO_ADVERSE unrealizedR={unrealizedR:0.00} threshold=-{TVM_MinAdverseMoveR:0.00}", ctx));
                return false;
            }

            if (stillValid)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    "[TVM][SKIP_REASON] STILL_VALID", ctx));
                return false;
            }

            if (!noRecoveryInWindow)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    "[TVM][SKIP_REASON] RECOVERY", ctx));
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    "[TVM][HOLD] Recovery detected", ctx));
                return false;
            }

            bool marketTrend = ctx.MarketTrend;
            bool atrShrinking = IsAtrShrinking(m5);

            if (barsSinceEntry <= 3)
            {
                return EvaluateEarlyPhase(
                    ctx,
                    barsSinceEntry,
                    atrShrinking,
                    marketTrend,
                    m5,
                    pos.TradeType,
                    structureBreakDetected,
                    strongOppositeImpulseDetected,
                    strongHtfConflictDetected,
                    noRecoveryInWindow,
                    slowSetup,
                    breakoutSetup);
            }

            if (barsSinceEntry <= 10)
            {
                return EvaluateDevelopmentPhase(
                    ctx,
                    barsSinceEntry,
                    marketTrend,
                    m5,
                    pos.TradeType,
                    structureBreakDetected,
                    strongOppositeImpulseDetected,
                    strongHtfConflictDetected,
                    noRecoveryInWindow,
                    slowSetup,
                    breakoutSetup);
            }

            return EvaluateMaturePhase(
                ctx,
                barsSinceEntry,
                marketTrend,
                m5,
                pos.TradeType,
                structureBreakDetected,
                strongOppositeImpulseDetected,
                strongHtfConflictDetected,
                noRecoveryInWindow);
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
            TradeType tradeType,
            bool structureBreak,
            bool strongOppositeImpulse,
            bool strongHtfConflict,
            bool noRecoveryInWindow,
            bool slowSetup,
            bool breakoutSetup)
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
                !slowSetup &&
                barsSinceEntry >= 4 &&
                ctx.MfeR < (breakoutSetup ? 0.20 : 0.15) &&
                ctx.MaeR > (breakoutSetup ? 0.20 : 0.25);

            if (noFollowThrough)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "NO_FOLLOW_THROUGH";
                return true;
            }

            bool noProgress = !slowSetup && barsSinceEntry >= 3 && ctx.MfeR < (breakoutSetup ? 0.12 : 0.10);
            bool adverseExpansion = ctx.MaeR > 0.35;
            bool momentumWeak = ctx.Adx_M5 < 20.0 || atrShrinking;
            bool fastAdverse = ctx.MaeR > 0.35 && barsSinceEntry <= 2;
            bool htfFail = strongHtfConflict && strongOppositeImpulse && noRecoveryInWindow;

            if (strongHtfConflict && !htfFail)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][SKIP_REASON] HTF_WEAK_CONFLICT", ctx));
            }

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

            if (htfFail)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "HTF_FAIL";
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][ALLOW_EXIT] reason=HTF_FAIL", ctx));
                return true;
            }

            return false;
        }

        private bool EvaluateDevelopmentPhase(
            PositionContext ctx,
            int barsSinceEntry,
            bool marketTrend,
            Bars m5,
            TradeType tradeType,
            bool structureBreak,
            bool strongOppositeImpulse,
            bool strongHtfConflict,
            bool noRecoveryInWindow,
            bool slowSetup,
            bool breakoutSetup)
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
                !slowSetup &&
                barsSinceEntry >= 3 &&
                ctx.MfeR < (breakoutSetup ? 0.20 : 0.15) &&
                ctx.MaeR > (breakoutSetup ? 0.20 : 0.25);

            if (noFollowThrough)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "NO_FOLLOW_THROUGH";
                return true;
            }

            if (!slowSetup && barsSinceEntry >= 4 && ctx.MfeR <= (breakoutSetup ? 0.10 : 0.05))
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
            bool htfFail = strongHtfConflict && strongOppositeImpulse && noRecoveryInWindow;

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM DEVELOPMENT] momentumDecay={momentumDecay} structureBreak={structureBreak} " +
                $"strongOppositeImpulse={strongOppositeImpulse} strongHtfConflict={strongHtfConflict} noRecovery={noRecoveryInWindow} htfFail={htfFail}");

            bool shouldExit =
                structureBreak ||
                strongOppositeImpulse ||
                htfFail;

            if (strongHtfConflict && !htfFail)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][SKIP_REASON] HTF_WEAK_CONFLICT", ctx));
            }

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM DECISION] phase=DEVELOPMENT invalidation={shouldExit} structureBreak={structureBreak} " +
                $"oppositeImpulse={strongOppositeImpulse} htfFail={htfFail} momentumDecay={momentumDecay}");

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
                if (structureBreak)
                    ctx.DeadTradeReason = "STRUCTURE_BREAK";
                else if (htfFail)
                    ctx.DeadTradeReason = "HTF_FAIL";
                else if (strongOppositeImpulse)
                    ctx.DeadTradeReason = "IMPULSE_REVERSAL";
                _bot.Print(TradeLogIdentity.WithPositionIds($"[TVM][EXIT_REASON] {ctx.DeadTradeReason}", ctx));
                _bot.Print(TradeLogIdentity.WithPositionIds($"[TVM][ALLOW_EXIT] reason={ctx.DeadTradeReason}", ctx));

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
            bool marketTrend,
            Bars m5,
            TradeType tradeType,
            bool structureBreak,
            bool strongOppositeImpulse,
            bool strongHtfConflict,
            bool noRecoveryInWindow)
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

            bool htfFail = strongHtfConflict && strongOppositeImpulse && noRecoveryInWindow;

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM MATURE] structureBreak={structureBreak} strongOppositeImpulse={strongOppositeImpulse} " +
                $"strongHtfConflict={strongHtfConflict} noRecovery={noRecoveryInWindow} htfFail={htfFail}");

            bool shouldExit =
                structureBreak ||
                strongOppositeImpulse ||
                htfFail;

            if (strongHtfConflict && !htfFail)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds("[TVM][SKIP_REASON] HTF_WEAK_CONFLICT", ctx));
            }

            LogTvmOncePerBar(
                ctx,
                ctx.LastTvmEvalBar,
                $"[TVM DECISION] phase=MATURE invalidation={shouldExit} structureBreak={structureBreak} " +
                $"oppositeImpulse={strongOppositeImpulse} htfFail={htfFail}");

            if (shouldExit)
            {
                ctx.IsDeadTrade = true;
                if (structureBreak)
                    ctx.DeadTradeReason = "STRUCTURE_BREAK";
                else if (htfFail)
                    ctx.DeadTradeReason = "HTF_FAIL";
                else if (strongOppositeImpulse)
                    ctx.DeadTradeReason = "IMPULSE_REVERSAL";
                _bot.Print(TradeLogIdentity.WithPositionIds($"[TVM][EXIT_REASON] {ctx.DeadTradeReason}", ctx));

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

        private double ComputeUnrealizedR(Position pos, double risk)
        {
            if (risk <= 0 || pos == null)
                return 0.0;

            double currentPrice = pos.TradeType == TradeType.Buy ? pos.Symbol.Bid : pos.Symbol.Ask;
            double move = pos.TradeType == TradeType.Buy ? currentPrice - pos.EntryPrice : pos.EntryPrice - currentPrice;
            return move / risk;
        }

        private bool IsStructuredBreak(TradeType tradeType, Bars m5, PositionContext ctx)
        {
            bool weakened = IsStructureWeakening(tradeType, m5);
            bool adverseEnough = ctx != null && ctx.MaeR >= 0.45;
            return weakened && adverseEnough;
        }

        private bool IsStrongOppositeImpulse(TradeType tradeType, Bars m5)
        {
            if (m5 == null || m5.Count < 6)
                return false;

            double close0 = m5.ClosePrices.Last(0);
            double open0 = m5.OpenPrices.Last(0);
            double body0 = Math.Abs(close0 - open0);
            if (body0 <= 0)
                return false;

            double avgBody = 0.0;
            int i = 1;
            while (i <= 4)
            {
                avgBody += Math.Abs(m5.ClosePrices.Last(i) - m5.OpenPrices.Last(i));
                i++;
            }

            avgBody /= 4.0;
            bool bodyDominant = body0 >= (avgBody * 1.5);
            bool againstDirection = tradeType == TradeType.Buy ? close0 < open0 : close0 > open0;

            if (tradeType == TradeType.Buy)
                return againstDirection && bodyDominant && close0 < m5.LowPrices.Last(1);

            return againstDirection && bodyDominant && close0 > m5.HighPrices.Last(1);
        }

        private bool IsHtfConflict(TradeType tradeType, Bars m15)
        {
            if (m15 == null || m15.Count < 3)
                return false;

            double c0 = m15.ClosePrices.Last(0);
            double c1 = m15.ClosePrices.Last(1);
            double c2 = m15.ClosePrices.Last(2);

            if (tradeType == TradeType.Buy)
                return c0 < c1 && c1 < c2;

            return c0 > c1 && c1 > c2;
        }

        private bool IsStrongHtfConflict(TradeType tradeType, Bars m15)
        {
            if (m15 == null || m15.Count < 4)
                return false;

            if (!IsHtfConflict(tradeType, m15))
                return false;

            double c0 = m15.ClosePrices.Last(0);
            double c3 = m15.ClosePrices.Last(3);
            double c1 = m15.ClosePrices.Last(1);
            double c2 = m15.ClosePrices.Last(2);
            double step1 = Math.Abs(c0 - c1);
            double step2 = Math.Abs(c1 - c2);
            double trendLeg = Math.Abs(c0 - c3);

            return trendLeg > 0 && (step1 + step2) > 0 && trendLeg >= ((step1 + step2) * 0.8);
        }

        private bool IsSlowDevelopmentSetup(string entryType)
        {
            if (string.IsNullOrWhiteSpace(entryType))
                return false;

            return entryType.IndexOf("FLAG", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entryType.IndexOf("PULLBACK", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsBreakoutSetup(string entryType)
        {
            if (string.IsNullOrWhiteSpace(entryType))
                return false;

            return entryType.IndexOf("BREAKOUT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entryType.IndexOf("RANGEBREAKOUT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   entryType.StartsWith("BR_", StringComparison.OrdinalIgnoreCase);
        }

        private bool RecentRecoveryDetected(TradeType tradeType, Bars m5, int lookbackBars)
        {
            if (m5 == null || lookbackBars < 1 || m5.Count < lookbackBars + 2)
                return false;

            int favorableSteps = 0;
            int i = 0;
            while (i < lookbackBars)
            {
                double cNow = m5.ClosePrices.Last(i);
                double cPrev = m5.ClosePrices.Last(i + 1);

                bool favorable = tradeType == TradeType.Buy ? cNow > cPrev : cNow < cPrev;
                if (favorable)
                    favorableSteps++;

                i++;
            }

            return favorableSteps >= lookbackBars;
        }
    }
}
