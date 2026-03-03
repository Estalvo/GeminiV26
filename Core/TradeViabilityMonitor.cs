using System;
using cAlgo.API;

namespace GeminiV26.Core
{
    /// <summary>
    /// Gemini V26 – Trade Viability Monitor (TVM)
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

            if (m5 == null || m5.Count < 5)
                return false;

            double risk = ctx.RiskPriceDistance;
            if (risk <= 0)
                return false;

            // =====================================================
            // 0️⃣ ASSET CLASS DETECTION
            // =====================================================

            string sym = pos.SymbolName ?? string.Empty;

            bool isFx =
                sym == "EURUSD" ||
                sym == "GBPUSD" ||
                sym == "USDJPY" ||
                sym == "AUDNZD" ||
                sym == "AUDUSD" ||
                sym == "NZDUSD" ||
                sym == "USDCHF" ||
                sym == "USDCAD" ||
                sym == "EURJPY" ||
                sym == "GBPJPY";

            bool isCrypto =
                sym == "BTCUSD" ||
                sym == "ETHUSD" ||
                sym == "BTCUSDT" ||
                sym == "ETHUSDT";

            bool isMetal =
                sym == "XAUUSD" ||
                sym == "XAGUSD" ||
                sym == "XPTUSD" ||
                sym == "XPDUSD";

            bool isIndex =
                sym == "NAS100" ||
                sym == "US30" ||
                sym == "SPX500" ||
                sym == "DE40" ||
                sym == "UK100" ||
                sym == "JP225";

            // fallback: ha nincs felismerve, akkor "INDEX-like" (gyors döntés) helyett inkább konzervatív METAL/INDEX közép
            string asset =
                isFx ? "FX" :
                isCrypto ? "CRYPTO" :
                isMetal ? "METAL" :
                isIndex ? "INDEX" :
                "UNKNOWN";

            // =====================================================
            // 0.1️⃣ ASSET-SPECIFIC THRESHOLDS (MFE/MAE/TIME/B2E)
            // =====================================================

            // MFE = minimum progress elvárás (dead/no follow-through)
            double mfeNoProgressR =
                isFx ? 0.12 :
                isCrypto ? 0.30 :
                isIndex ? 0.25 :
                isMetal ? 0.20 :
                0.20;

            // MAE = ellenirányú mozgás küszöb (veszély)
            double maeAdverseR =
                isFx ? 0.40 :
                isCrypto ? 0.25 :
                isIndex ? 0.30 :
                isMetal ? 0.30 :
                0.35;

            // Minimum időablak percben (hogy ne vágjunk túl korán)
            double minMinutesOpen =
                isFx ? 25 :          // FX: türelmesebb
                isCrypto ? 10 :      // crypto: gyors döntés
                isIndex ? 12 :       // index: gyors döntés, de ne túl agresszív
                isMetal ? 12 :       // metal: spike-ok miatt közepes
                15;

            // Back-to-entry tolerancia R-ben (ha visszajött entry környékére)
            double backToEntryBandR =
                isFx ? 0.18 :
                isCrypto ? 0.12 :
                isIndex ? 0.12 :
                isMetal ? 0.15 :
                0.15;

            // Danger threshold (intelligens threshold) – asset szerint
            int dangerThreshold =
                isFx ? 3 :
                isCrypto ? 2 :   // crypto-n gyorsabban vágunk
                isIndex ? 3 :
                isMetal ? 3 :
                3;

            // Profit rescue paraméterek (TP1-proximity based)
            double rescueTriggerFactor =
                isCrypto ? 0.70 :   // crypto: ha már majdnem TP1, gyorsabban mentsük
                isIndex ? 0.65 :
                isMetal ? 0.65 :
                0.65;

            double rescueFadeFactor =
                isCrypto ? 0.55 :   // crypto: szigorúbb visszaesés-érzékenység
                0.60;

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

            // nincs valódi progress (asset-aware)
            bool noProgress = ctx.MfeR < mfeNoProgressR;

            // jelentős ellenirányú mozgás (asset-aware)
            bool adverseGrowing = ctx.MaeR > maeAdverseR;

            // időablak (asset-aware)
            double minutesOpen =
                (DateTime.UtcNow - ctx.EntryTime).TotalMinutes;

            bool enoughTime = minutesOpen >= minMinutesOpen;

            // visszajött entry közelébe (asset-aware)
            bool backToEntry =
                Math.Abs(currentPrice - ctx.EntryPrice)
                < risk * backToEntryBandR;

            // =====================================================
            // 3️⃣ STRUCTURE DAMAGE
            // =====================================================

            bool structureBroken = IsStructureWeakening(pos, m5);

            // =====================================================
            // 4️⃣ MOMENTUM COLLAPSE
            // =====================================================

            bool momentumFade = IsMomentumFading(m5);

            // =====================================================
            // 5️⃣ CRYPTO / INDEX EARLY "NO-IMPULSE" SAFETY (extra guard)
            //    (nem törlünk, csak pontosítunk és kiegészítünk)
            // =====================================================

            // crypto/index: ha nagyon gyorsan kiderül, hogy nincs follow-through,
            // akkor ne várjunk a teljes "danger matrix"-ra.
            if ((isCrypto || isIndex) && enoughTime)
            {
                bool fastDead =
                    noProgress &&
                    (ctx.MaeR > (maeAdverseR * 0.90)); // kicsit agresszívebb, mint a fő MAE küszöb

                if (fastDead)
                {
                    ctx.IsDeadTrade = true;
                    ctx.DeadTradeReason = $"{asset}_FAST_DEAD_NO_IMPULSE";
                    _bot?.Print($"[TVM {asset}] EARLY EXIT | reason={ctx.DeadTradeReason} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} min={minMinutesOpen:0}m");
                    return true;
                }
            }

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

            // ha teljesen dead trade: gyors kill (asset-aware)
            // (a korábbi !isFx ágat nem töröljük, hanem pontosítjuk és kibővítjük)
            if (!isFx && noProgress && adverseGrowing && enoughTime)
            {
                ctx.IsDeadTrade = true;
                ctx.DeadTradeReason = $"{asset}_NO_PROGRESS_ADVERSE";
                _bot?.Print($"[TVM {asset}] DEAD TRADE EXIT | reason={ctx.DeadTradeReason} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} danger={danger}/{dangerThreshold}");
                return true;
            }

            // FX: no follow-through + structure/momentum kombináció esetén is legyen "smart kill"
            if (isFx && enoughTime)
            {
                bool fxNoFollowThrough =
                    noProgress &&
                    (structureBroken || momentumFade || backToEntry) &&
                    ctx.MaeR > (maeAdverseR * 0.80);

                if (fxNoFollowThrough)
                {
                    ctx.IsDeadTrade = true;
                    ctx.DeadTradeReason = "FX_NO_FOLLOW_THROUGH";
                    _bot?.Print($"[TVM FX] EARLY EXIT | reason={ctx.DeadTradeReason} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} danger={danger}/{dangerThreshold}");
                    return true;
                }
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
                        double rescueTriggerR = tp1R * rescueTriggerFactor;   // asset-aware

                        // NOTE: pos.Pips * PipSize = ármozgás "price" egységben
                        // Itt abs-t használunk, mert a logika a visszaesés arányára figyel.
                        double currentR =
                            Math.Abs(pos.Pips * pos.Symbol.PipSize) / risk;

                        bool wasNearTp1 = ctx.MfeR >= rescueTriggerR;
                        bool nowFading = currentR < ctx.MfeR * rescueFadeFactor;   // asset-aware visszaesés
                        bool structureWeak = IsStructureWeakening(pos, m5);

                        if (wasNearTp1 && nowFading && structureWeak)
                        {
                            ctx.DeadTradeReason = $"{asset}_RESCUE_EXIT";
                            _bot?.Print($"[TVM {asset}] RESCUE EXIT | reason={ctx.DeadTradeReason} | tp1R={tp1R:0.00} mfeR={ctx.MfeR:0.00} curR={currentR:0.00}");
                            return true; // RESCUE EXIT
                        }
                    }
                }
            }

            // normál intelligens threshold (asset-aware)
            bool exit = danger >= dangerThreshold;

            if (exit)
            {
                // ok megjelölés: nem feltétlen "dead trade", lehet momentum/structure collapse is
                if (string.IsNullOrEmpty(ctx.DeadTradeReason))
                    ctx.DeadTradeReason = $"{asset}_DANGER_{danger}_OF_{dangerThreshold}";

                _bot?.Print($"[TVM {asset}] THRESHOLD EXIT | reason={ctx.DeadTradeReason} | danger={danger}/{dangerThreshold} | mfeR={ctx.MfeR:0.00} maeR={ctx.MaeR:0.00} back={backToEntry} struct={structureBroken} mom={momentumFade}");
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