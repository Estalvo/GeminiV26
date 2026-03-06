using System;
using cAlgo.API;

namespace GeminiV26.Core
{
    /// <summary>
    /// Gemini V26 – Trade Viability Monitor (TVM 2.0)
    /// Asset-aware: FX / INDEX / METAL / CRYPTO
    ///
    /// NOTE: Ez a modul csak akkor él, ha az ExitManager(ek) hívják.
    /// </summary>
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

            // =====================================================
            // 0️⃣ ASSET CLASS DETECTION
            // =====================================================
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
            // 0.1️⃣ ASSET-SPECIFIC THRESHOLDS
            // =====================================================

            double mfeNoProgressR =
                isFx ? 0.12 :
                isCrypto ? 0.30 :
                isIndex ? 0.25 :
                isMetal ? 0.20 :
                0.20;

            double maeAdverseR =
                isFx ? 0.40 :
                isCrypto ? 0.25 :
                isIndex ? 0.30 :
                isMetal ? 0.30 :
                0.35;

            double minMinutesOpen =
                isFx ? 35 :
                isCrypto ? 10 :
                isIndex ? 12 :
                isMetal ? 12 :
                15;

            double backToEntryBandR =
                isFx ? 0.18 :
                isCrypto ? 0.12 :
                isIndex ? 0.12 :
                isMetal ? 0.15 :
                0.15;

            int dangerThreshold =
                isFx ? 4 :
                isCrypto ? 2 :
                isIndex ? 3 :
                isMetal ? 3 :
                3;

            double rescueTriggerFactor =
                isCrypto ? 0.70 :
                isIndex ? 0.65 :
                isMetal ? 0.65 :
                0.65;

            double rescueFadeFactor =
                isCrypto ? 0.55 :
                0.60;

            // Counter-impulse / sweep hold
            double counterImpulseHoldMaeR =
                isCrypto ? 0.45 :
                isIndex ? 0.40 :
                isMetal ? 0.40 :
                0.35;

            double counterImpulseRecoverBandR =
                isCrypto ? 0.18 :
                isIndex ? 0.18 :
                isMetal ? 0.20 :
                0.22;

            int counterImpulseMaxBarsM5 =
                isCrypto ? 4 :
                isIndex ? 4 :
                5;

            int counterImpulseMinBarsM5 =
                isCrypto ? 2 :
                isIndex ? 2 :
                isMetal ? 2 :
                2;

            // =====================================================
            // Bars since entry (M5) – deterministic O(1)
            // =====================================================

            ctx.BarsSinceEntryM5 = (int)Math.Max(
                1,
                (_bot.Server.Time - ctx.EntryTime).TotalSeconds / 300.0
            );

            // =====================================================
            // 1️⃣ PRICE + MFE/MAE UPDATE
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
            // 2️⃣ CORE FLAGS
            // =====================================================

            bool noProgress = ctx.MfeR < mfeNoProgressR;
            bool adverseGrowing = ctx.MaeR > maeAdverseR;

            double minutesOpen = (_bot.Server.Time - ctx.EntryTime).TotalMinutes;
            bool enoughTime = minutesOpen >= minMinutesOpen;

            bool backToEntry =
                Math.Abs(currentPrice - ctx.EntryPrice) < risk * backToEntryBandR;

            bool structureBroken = IsStructureWeakening(pos, m5);
            bool momentumFade = IsMomentumFading(m5);

            bool fxGraceWindow = isFx && ctx.BarsSinceEntryM5 < 4;
            // -----------------------------------------------------
            // Optional audit snapshot (ritkított log: csak 1x / bar-t érdemes, ExitManager oldalon)
            // _bot?.Print($"[TVM {asset}] SNAP | bars={ctx.BarsSinceEntryM5} min={minutesOpen:0.0} mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00}");

            // =====================================================
            // DEBUG SNAPSHOT (early trade diagnostics)
            // =====================================================

            if (ctx.BarsSinceEntryM5 <= 6)
            {
                _bot.Print(
                    $"[TVM SNAP {asset}] " +
                    $"bars={ctx.BarsSinceEntryM5} " +
                    $"min={minutesOpen:0.0} " +
                    $"mfeR={ctx.MfeR:0.00} " +
                    $"maeR={ctx.MaeR:0.00} " +
                    $"noProg={noProgress} " +
                    $"advGrow={adverseGrowing} " +
                    $"backEntry={backToEntry} " +
                    $"structWeak={structureBroken} " +
                    $"momFade={momentumFade}"
                );
            }

            // =====================================================
            // 2.5️⃣ COUNTER-IMPULSE / SWEEP HOLD
            // =====================================================

            if ((isCrypto || isIndex || isMetal) &&
                ctx.BarsSinceEntryM5 >= counterImpulseMinBarsM5 &&
                ctx.BarsSinceEntryM5 <= counterImpulseMaxBarsM5)
            {
                bool hadCounterImpulse = ctx.MaeR >= counterImpulseHoldMaeR;

                bool recoveredTowardEntry =
                    Math.Abs(currentPrice - ctx.EntryPrice) < risk * counterImpulseRecoverBandR;

                bool structureImproving = IsStructureImproving(pos, m5);

                if (hadCounterImpulse && recoveredTowardEntry && structureImproving)
                {
                    _bot?.Print($"[TVM {asset}] HOLD (SWEEP) | maeR={ctx.MaeR:0.00} mfeR={ctx.MfeR:0.00} barsM5={ctx.BarsSinceEntryM5} recovered={recoveredTowardEntry} structImp={structureImproving}");
                    return false;
                }
            }

            // =====================================================
            // 3️⃣ FAST DEAD (Crypto/Index)
            // =====================================================

            if ((isCrypto || isIndex) && enoughTime)
            {
                bool fastDead =
                    noProgress &&
                    (ctx.MaeR > (maeAdverseR * 0.90));

                if (fastDead)
                {
                    ctx.IsDeadTrade = true;
                    ctx.DeadTradeReason = $"{asset}_FAST_DEAD_NO_IMPULSE";
                    _bot?.Print($"[TVM {asset}] EARLY EXIT | reason={ctx.DeadTradeReason} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} min={minMinutesOpen:0}m barsM5={ctx.BarsSinceEntryM5}");
                    return true;
                }
            }

            // =====================================================
            // 4️⃣ INTELLIGENT DANGER MATRIX
            // =====================================================

            int danger = 0;

            if (noProgress && enoughTime) danger++;
            if (adverseGrowing) danger++;
            if (backToEntry) danger++;
            if (structureBroken) danger++;
            if (momentumFade) danger++;

            // =====================================================
            // 5️⃣ SMART KILLS
            // =====================================================

            if (!isFx && noProgress && adverseGrowing && enoughTime)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = $"{asset}_NO_PROGRESS_ADVERSE";
                _bot?.Print($"[TVM {asset}] DEAD TRADE EXIT | reason={ctx.DeadTradeReason} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} danger={danger}/{dangerThreshold}");
                return true;
            }

            if (isFx && enoughTime && !fxGraceWindow)
            {
                bool fxNoFollowThrough =
                    noProgress &&
                    backToEntry &&
                    (structureBroken || momentumFade) &&
                    ctx.MaeR > (maeAdverseR * 0.95);

                if (fxNoFollowThrough)
                {
                    ctx.IsDeadTrade = true;
                    ctx.DeadTradeReason = "FX_NO_FOLLOW_THROUGH";
                    _bot?.Print($"[TVM FX] EARLY EXIT | reason={ctx.DeadTradeReason} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} danger={danger}/{dangerThreshold} barsM5={ctx.BarsSinceEntryM5}");
                    return true;
                }
            }

            // =====================================================
            // 6️⃣ PROFIT RESCUE (TP1-proximity based)
            // =====================================================

            if (!ctx.Tp1Hit && ctx.Tp1R > 0)
            {
                bool enoughBarsForRescue = ctx.BarsSinceEntryM5 >= 4;
                if (enoughBarsForRescue)
                {
                    double tp1R = ctx.Tp1R;
                    double rescueTriggerR = tp1R * rescueTriggerFactor;

                    double currentR = favorableR;

                    bool wasNearTp1 = ctx.MfeR >= rescueTriggerR;
                    bool nowFading = currentR < ctx.MfeR * rescueFadeFactor;
                    bool structureWeak = IsStructureWeakening(pos, m5);

                    if (wasNearTp1 && nowFading && structureWeak)
                    {
                        ctx.DeadTradeReason = $"{asset}_RESCUE_EXIT";
                        _bot?.Print($"[TVM {asset}] RESCUE EXIT | reason={ctx.DeadTradeReason} | tp1R={tp1R:0.00} mfeR={ctx.MfeR:0.00} curR={currentR:0.00}");
                        return true;
                    }
                }
            }

            // =====================================================
            // 7️⃣ THRESHOLD EXIT
            // =====================================================
            // FX grace window – az első pár M5 barban ne vágjunk ki trade-et
            if (isFx && fxGraceWindow)
                return false;
                
            bool exit = danger >= dangerThreshold;

            if (exit)
            {
                if (string.IsNullOrEmpty(ctx.DeadTradeReason))
                    ctx.DeadTradeReason = $"{asset}_DANGER_{danger}_OF_{dangerThreshold}";

                _bot?.Print($"[TVM {asset}] THRESHOLD EXIT | reason={ctx.DeadTradeReason} | danger={danger}/{dangerThreshold} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} back={backToEntry} struct={structureBroken} mom={momentumFade} barsM5={ctx.BarsSinceEntryM5}");
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
            double c3 = m5.ClosePrices.Last(3);

            if (pos.TradeType == TradeType.Buy)
                return c0 < c1 && c1 <= c2 && c2 <= c3;

            return c0 > c1 && c1 >= c2 && c2 >= c3;
        }

        private bool IsStructureImproving(Position pos, Bars m5)
        {
            if (m5.Count < 4)
                return false;

            double c0 = m5.ClosePrices.Last(0);
            double c1 = m5.ClosePrices.Last(1);
            double c2 = m5.ClosePrices.Last(2);
            double c3 = m5.ClosePrices.Last(3);

            if (pos.TradeType == TradeType.Buy)
                return c0 > c1 && c1 >= c2 && c2 >= c3;

            return c0 < c1 && c1 <= c2 && c2 <= c3;
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