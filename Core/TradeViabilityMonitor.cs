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

            if (m5 == null || m5.Count < 5)
                return false;

            double risk = ctx.RiskPriceDistance;
            if (risk <= 0)
                return false;

            bool isFx =
                pos.SymbolName == "EURUSD" ||
                pos.SymbolName == "GBPUSD" ||
                pos.SymbolName == "USDJPY" ||
                pos.SymbolName == "AUDNZD" ||
                pos.SymbolName == "AUDUSD" ||
                pos.SymbolName == "NZDUSD" ||
                pos.SymbolName == "USDCHF" ||
                pos.SymbolName == "USDCAD" ||
                pos.SymbolName == "EURJPY" ||
                pos.SymbolName == "GBPJPY";

            // =====================================================
            // 1️⃣ UPDATE MFE / MAE (core state)
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

            // =====================================================
            // 2️⃣ DEAD TRADE LOGIC (core idea)
            // =====================================================

            // nincs valódi progress
            bool noProgress =
                isFx
                    ? ctx.MfeR < 0.10
                    : ctx.MfeR < 0.20;

            // jelentős ellenirányú mozgás
            bool adverseGrowing = ctx.MaeR > 0.35;

            // időablak (legalább 3 M5 bar)
            double minutesOpen =
                (DateTime.UtcNow - ctx.EntryTime).TotalMinutes;

            bool enoughTime =
                isFx
                    ? minutesOpen >= 25   // FX: 5 bar
                    : minutesOpen >= 15;  // index, crypto, metal

            // visszajött entry közelébe
            bool backToEntry =
                Math.Abs(currentPrice - ctx.EntryPrice)
                < risk * 0.15;

            // =====================================================
            // 3️⃣ STRUCTURE DAMAGE
            // =====================================================

            bool structureBroken = IsStructureWeakening(pos, m5);

            // =====================================================
            // 4️⃣ MOMENTUM COLLAPSE
            // =====================================================

            bool momentumFade = IsMomentumFading(m5);

            // =====================================================
            // INTELLIGENT MATRIX
            // =====================================================

            int danger = 0;

            if (noProgress && enoughTime) danger++;
            if (adverseGrowing) danger++;
            if (backToEntry) danger++;
            if (structureBroken) danger++;
            if (momentumFade) danger++;

            // -----------------------------------------------------
            // SMART THRESHOLD
            // -----------------------------------------------------

            // ha teljesen dead trade: gyors kill
            if (!isFx && noProgress && adverseGrowing && enoughTime)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = "NO_PROGRESS_ADVERSE";
                return true;
            }

            // ==========================================
            // 7️⃣ PROFIT RESCUE (TP1-proximity based)
            // ==========================================

            if (!ctx.Tp1Hit && ctx.Tp1R > 0)
            {
                bool enoughBarsForRescue = ctx.BarsSinceEntryM5 >= 4;

                if (enoughBarsForRescue)
                {
                    {
                        double tp1R = ctx.Tp1R;
                        double rescueTriggerR = tp1R * 0.65;   // 65% TP1

                        double currentR =
                            Math.Abs(pos.Pips * pos.Symbol.PipSize) / risk;

                        bool wasNearTp1 = ctx.MfeR >= rescueTriggerR;
                        bool nowFading = currentR < ctx.MfeR * 0.6;   // 40% visszaesés
                        bool structureWeak = IsStructureWeakening(pos, m5);

                        if (wasNearTp1 && nowFading && structureWeak)
                            return true; // RESCUE EXIT
                    }
                }

            }
            // normál intelligens threshold
            return danger >= 3;
        }

        private bool IsStructureWeakening(Position pos, Bars m5)
        {
            if (m5.Count < 4)
                return false;

            double c0 = m5.ClosePrices.Last(0);
            double c1 = m5.ClosePrices.Last(1);
            double c2 = m5.ClosePrices.Last(2);
            double c3 = m5.ClosePrices.Last(3);

            if (pos.TradeType == TradeType.Buy)
                return c0 < c1 && c1 <= c2 && c2 <= c3;

            return c0 > c1 && c1 >= c2 && c2 >= c3;
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
