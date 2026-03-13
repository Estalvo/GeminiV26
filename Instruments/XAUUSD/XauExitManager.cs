// =========================================================
// GEMINI V26 – XAUUSD ExitManager
// Phase 3.7.x – RULEBOOK 1.0 COMPLIANT
//
// SZEREP:
// - Pozíció menedzsment XAUUSD-re (TP1, BE, Trailing, Early Exit)
// - A PositionContext a "single source of truth"
//
// ALAPELVEK:
// - TP1 + BE + trailing kizárólag OnTick()
// - ExitManager NEM gate-el belépést (az TradeCore feladata)
// - Paraméterek (TP1R bucket, BE offset) profilból jönnek (Matrix → Profile)
//
// KRITIKUS JAVÍTÁSOK:
// - NINCS class-szintű "tp1R = ctx..." jellegű kód
// - TP1Hit csak ExecuteTp1-ben állítódik (nincs dupla set)
// - R-distance SSOT: preferáljuk ctx.Tp1Price / ctx.RiskPriceDistance-t
// =========================================================

using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.TradeManagement;
using GeminiV26.Data;
using GeminiV26.Data.Models;
using GeminiV26.EntryTypes.METAL; // XAU_InstrumentProfile + XAU_InstrumentMatrix (EntryTypes/Metal)
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API.Indicators;

namespace GeminiV26.Instruments.XAUUSD
{
    public class XauExitManager
    {
        private readonly Robot _bot;
        private readonly EventLogger _eventLogger;

        // Profile (matrixból) – TP1/BE paraméterek innen jönnek
        private readonly XAU_InstrumentProfile _profile;
        private AverageTrueRange _atr;

        private const bool DebugTp1 = false;
        private readonly TradeViabilityMonitor _tvm;
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;
        // PositionId → Context
        private readonly Dictionary<long, PositionContext> _contexts = new();

        public XauExitManager(Robot bot)
        {
            _bot = bot;
            _eventLogger = new EventLogger(bot.SymbolName);
            _tvm = new TradeViabilityMonitor(bot);
            _trendTradeManager = new TrendTradeManager(_bot, _bot.Bars);
            _adaptiveTrailingEngine = new AdaptiveTrailingEngine(_bot);
            _structureTracker = new StructureTracker(_bot, _bot.Bars);

            // Profile betöltés (SSOT policy)
            _profile = XAU_InstrumentMatrix.Get(bot.SymbolName);

            // ATR indikátor – EGYSZER létrehozva
            _atr = bot.Indicators.AverageTrueRange(
                bot.Bars,
                14,
                MovingAverageType.Exponential
            );
        }

        // TradeCore/Executor hívja sikeres belépés után
        public void RegisterContext(PositionContext ctx)
        {
            _contexts[ctx.PositionId] = ctx;
        }

        // =====================================================
        // BAR-LEVEL EXIT (jelenleg csak early exit placeholder)
        // =====================================================
        public void OnBar(Position pos)
        {
            if (!_contexts.TryGetValue(pos.Id, out var ctx))
                return;

            // =====================================================
            // M5 bar counter (TVM / rescue / viability window)
            // =====================================================
            ctx.BarsSinceEntryM5++;

            if (ctx.Tp1Hit)
                TryEarlyExit(pos, ctx);
        }

        private void Debug(string msg)
        {
            if (DebugTp1)
                _bot.Print(msg);
        }

        // =====================================================
        // TICK-LEVEL EXIT
        // - TP1
        // - BE
        // - Trailing (TP1 után)
        // =====================================================
        public void OnTick()
        {
            foreach (var ctx in _contexts.Values)
            {
                var pos = _bot.Positions.FirstOrDefault(p => p.Id == ctx.PositionId);
                if (pos == null)
                    continue;

                if (ctx.RiskPriceDistance <= 0)
                    continue;

                if (!pos.StopLoss.HasValue)
                    continue;

                var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
                if (sym == null)
                    continue;

                // -------------------------------------------------
                // SSOT: TP1Price preferált, ha executor már kiszámolta.
                // Ha nincs TP1Price, akkor számolunk:
                // - prefer: ctx.RiskPriceDistance
                // - fallback: pos.EntryPrice vs pos.StopLoss (végső)
                // -------------------------------------------------

                double rDist = GetRiskDistance(pos, ctx);
                if (rDist <= 0)
                    continue;

                // =========================
                // TP1 (TP1 előtt nincs trailing)
                // =========================
                if (!ctx.Tp1Hit)
                {
                    double tp1R = ResolveTp1R(ctx);

                    double tp1Price = ctx.Tp1Price > 0
                        ? ctx.Tp1Price
                        : (pos.TradeType == TradeType.Buy
                            ? pos.EntryPrice + rDist * tp1R
                            : pos.EntryPrice - rDist * tp1R);

                    if (ctx.Tp1Price <= 0)
                        ctx.Tp1Price = tp1Price;

                    if (ctx.Tp1R <= 0)
                        ctx.Tp1R = tp1R;

                    double priceNow =
                        pos.TradeType == TradeType.Buy
                            ? sym.Bid
                            : sym.Ask;

                    bool hit =
                        pos.TradeType == TradeType.Buy
                            ? sym.Bid >= tp1Price
                            : sym.Ask <= tp1Price;

                    if (hit)
                    {
                        _bot.Print($"[EXIT] TP1 HIT symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={priceNow} tp1={tp1Price}");
                        ExecuteTp1(pos, ctx, rDist);
                        return;
                    }

                    // =========================
                    // TVM – Early Exit (TP1 előtt)
                    // =========================
                    {
                        var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5, pos.SymbolName);
                        var m15 = _bot.MarketData.GetBars(TimeFrame.Minute15, pos.SymbolName);

                        if (_tvm.ShouldEarlyExit(ctx, pos, m5, m15))
                        {
                            _bot.Print(
                                $"[XAU TVM EXIT] pos={pos.Id} " +
                                $"reason={ctx.DeadTradeReason} " +
                                $"MFE_R={ctx.MfeR:0.00} MAE_R={ctx.MaeR:0.00}"
                            );

                            _bot.ClosePosition(pos);

                            _eventLogger.Log(new EventRecord
                            {
                                EventTimestamp = DateTime.UtcNow,
                                Symbol = _bot.SymbolName,
                                EventType = "EXIT_TVM",
                                PositionId = ctx.PositionId,
                                Confidence = ctx.FinalConfidence,
                                Reason = ctx.DeadTradeReason,
                                Extra = "TVM",
                                RValue = ctx.MaeR
                            });

                            return;
                        }
                    }

                    continue;
                }

                // =========================
                // TRAILING (TP1 után)
                // =========================
                                var profile = TrailingProfiles.ResolveBySymbol(pos.SymbolName);
                var structure = _structureTracker.GetSnapshot();
                var decision = _trendTradeManager.Evaluate(pos, ctx, profile, structure);

                ctx.PostTp1TrendScore = decision.Score;
                ctx.PostTp1TrendState = decision.State.ToString();
                ctx.PostTp1TrailingMode = decision.TrailingMode.ToString();

                TryExtendTp2(pos, ctx, decision);
                _bot.Print($"[EXIT] TRAILING ACTIVE symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} sl={pos.StopLoss} tp={pos.TakeProfit}");
                _adaptiveTrailingEngine.Apply(pos, ctx, decision, structure, profile);
            }
        }

        // =====================================================
        // TP1 EXECUTION
        // =====================================================
        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return;

            // Partial close fraction: ctx-ből (executor beállította)
            double frac = ctx.Tp1CloseFraction;
            if (frac <= 0 || frac >= 1) frac = 0.40;

            double rawUnitsD = pos.VolumeInUnits * frac;
            long flooredUnits = (long)Math.Floor(rawUnitsD);
            long closeVolume = (long)sym.NormalizeVolumeInUnits(flooredUnits, RoundingMode.Down);

            if (closeVolume < sym.VolumeInUnitsMin)
                return;

            var closeResult = _bot.ClosePosition(pos, closeVolume);
            if (!closeResult.IsSuccessful)
            {
                _bot.Print($"[EXIT] PARTIAL CLOSE failed symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} tp1={ctx.Tp1Price}");
                return;
            }

            _bot.Print($"[EXIT] PARTIAL CLOSE executed symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} closedUnits={closeVolume}");

            // TP1 state (SSOT) – csak itt állítjuk
            ctx.Tp1Hit = true;

            // Remaining volume (analytics + rehydrate friendliness)
            ctx.Tp1ClosedVolumeInUnits = closeVolume;
            ctx.RemainingVolumeInUnits =
                Math.Max(0, pos.VolumeInUnits - closeVolume);

            // BE (profilból)
            ApplyBreakEven(pos, ctx, rDist);

            // Log TP1 event (nem loss!)
            _eventLogger.Log(new EventRecord
            {
                EventTimestamp = DateTime.UtcNow,
                Symbol = _bot.SymbolName,
                EventType = "EXIT_TP1",
                PositionId = ctx.PositionId,
                Confidence = ctx.FinalConfidence,
                Reason = "TP1_HIT",
                Extra = "ExitManager",
                RValue = ctx.Tp1R > 0 ? ctx.Tp1R : (double?)null
            });
        }

        private void ApplyBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return;

            double beOffsetR = _profile.BeOffsetR;
            if (beOffsetR <= 0) beOffsetR = 0.10;

            double bePrice =
                pos.TradeType == TradeType.Buy
                    ? pos.EntryPrice + rDist * beOffsetR
                    : pos.EntryPrice - rDist * beOffsetR;

            ctx.BePrice = bePrice;
            double newSl = bePrice;
            _bot.Print($"[EXIT] BE MOVE applied symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} be={bePrice}");

            _bot.ModifyPosition(
                pos,
                Normalize(bePrice, sym.TickSize, sym.Digits),
                pos.TakeProfit
            );
        }

        // =====================================================
        // TRAILING (TP1 után)
        // Megjegyzés: most még a meglévő "pct" logikát hagyjuk,
        // később át lehet kötni ATR-alapra profile multiplierekkel.
        // =====================================================
        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            // csak TP1 után
            if (!ctx.Tp1Hit)
                return;

            if (!pos.StopLoss.HasValue)
                return;

            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return;

            // ===== ATR (előre inicializált indikátor!) =====
            double atrPrice = _atr.Result.LastValue;
            if (atrPrice <= 0)
                return;

            // ===== PROFIL SZERINTI ATR SZORZÓ =====
            double atrMult =
                ctx.TrailingMode == TrailingMode.Loose ? _profile.TrailAtrLoose :
                ctx.TrailingMode == TrailingMode.Normal ? _profile.TrailAtrNormal :
                                                          _profile.TrailAtrTight;

            double trailDistPrice = atrPrice * atrMult;

            // aktuális ár
            double priceNow =
                pos.TradeType == TradeType.Buy
                    ? sym.Bid
                    : sym.Ask;

            // ===== PROGRESS SZŰRŐ (ATR-ALAPÚ, STABIL) =====
            double progressDist =
                pos.TradeType == TradeType.Buy
                    ? priceNow - pos.StopLoss.Value
                    : pos.StopLoss.Value - priceNow;

            // legalább 0.25 ATR előny kell
            if (progressDist < atrPrice * 0.25)
                return;

            // ===== ÚJ SL =====
            double newSl =
                pos.TradeType == TradeType.Buy
                    ? priceNow - trailDistPrice
                    : priceNow + trailDistPrice;

            // ===== MINIMÁLIS JAVULÁS (PIPS) =====
            double improvePips =
                pos.TradeType == TradeType.Buy
                    ? (newSl - pos.StopLoss.Value) / sym.PipSize
                    : (pos.StopLoss.Value - newSl) / sym.PipSize;

            if (improvePips < _profile.MinTrailImprovePips)
                return;

            _bot.ModifyPosition(
                 pos,
                Normalize(newSl, sym.TickSize, sym.Digits),
                pos.TakeProfit
            );
        }

        // =====================================================
        // HELPERS
        // =====================================================
        private double Normalize(double price, double tickSize, int digits)
        {
            double steps = Math.Round(price / tickSize);
            return Math.Round(steps * tickSize, digits);
        }

        private double ResolveTp1R(PositionContext ctx)
        {
            // Ha executor már beállította (pl. fix 0.25), tiszteletben tartjuk
            if (ctx.Tp1R > 0)
                return ctx.Tp1R;

            // Profil bucket
            if (ctx.FinalConfidence >= 85) return _profile.Tp1R_High;
            if (ctx.FinalConfidence >= 70) return _profile.Tp1R_Normal;
            return _profile.Tp1R_Low;
        }

        private double GetRiskDistance(Position pos, PositionContext ctx)
        {
            // 1) Legjobb: ctx.RiskPriceDistance (belépéskori SSOT)
            if (ctx.RiskPriceDistance > 0)
                return ctx.RiskPriceDistance;

            // 2) Ha nincs: próbáljuk a “kanonikus” SL snapshotot (rehydrate / executor töltheti)
            if (ctx.LastStopLossPrice.HasValue && ctx.LastStopLossPrice.Value > 0 && ctx.EntryPrice > 0)
            {
                double d = Math.Abs(ctx.EntryPrice - ctx.LastStopLossPrice.Value);
                if (d > 0) return d;   // <-- marad double!
            }

            // 3) Végső fallback (nem ideális): entryPrice vs aktuális SL
            // TP1 előtt még oké, TP1 után torzulhat, de TP1 után már Tp1Hit=true
            double d2 = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);
            return d2 > 0 ? d2 : 0;
        }

        private bool TryEarlyExit(Position pos, PositionContext ctx)
        {
            // jelenleg változatlan / kikapcsolt
            return false;
        }

        // =====================================================
        // REHYDRATE (restart után)
        // =====================================================
        public void RehydrateFromLivePositions(Robot bot)
        {
            foreach (var pos in bot.Positions)
            {
                if (SymbolRouting.ResolveInstrumentClass(pos.SymbolName) != InstrumentClass.METAL)
                    continue;

                if (!pos.StopLoss.HasValue)
                    continue;

                // Risk snapshot (fallback): entry vs current SL
                double rDist = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);
                if (rDist <= 0)
                    continue;

                var ctx = new PositionContext
                {
                    PositionId = pos.Id,
                    Symbol = pos.SymbolName,
                    EntryPrice = pos.EntryPrice,

                    EntryVolumeInUnits = pos.VolumeInUnits,
                    RemainingVolumeInUnits = pos.VolumeInUnits,
                    InitialVolumeInUnits = pos.VolumeInUnits,

                    // SSOT snapshot
                    RiskPriceDistance = rDist,
                    LastStopLossPrice = pos.StopLoss.Value,

                    Tp1Hit = false,
                    IsRehydrated = true
                };

                // AGENTS rule: PositionContext létrehozás után azonnal számoljuk a FinalConfidence-t.
                ctx.ComputeFinalConfidence();

                // TP1R becslés rehydrate-nél (nincs FC, ezért normal bucket)
                ctx.Tp1R = _profile.Tp1R_Normal;
                ctx.Tp1CloseFraction = 0.40;

                RegisterContext(ctx);
                bot.Print($"[XAU REHYDRATE] pos={pos.Id}");
            }
        }

        private void TryExtendTp2(Position pos, PositionContext ctx, TrendDecision decision)
        {
            if (!decision.AllowTp2Extension || !ctx.Tp2Price.HasValue || !ctx.Tp2Price.Value.Equals(pos.TakeProfit ?? ctx.Tp2Price.Value))
            {
                if (!decision.AllowTp2Extension)
                    _bot.Print("[TTM] TP2 extension skipped=notAllowed");
                return;
            }

            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            double baseR = ctx.Tp2R > 0 ? ctx.Tp2R : 1.0;
            double desiredR = baseR * decision.Tp2ExtensionMultiplier;
            double currentR = ctx.Tp2ExtensionMultiplierApplied > 0 ? baseR * ctx.Tp2ExtensionMultiplierApplied : baseR;

            if (desiredR <= currentR + 0.0001)
            {
                _bot.Print("[TTM] TP2 extension skipped=no progression");
                return;
            }

            double newTp = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + ctx.RiskPriceDistance * desiredR
                : pos.EntryPrice - ctx.RiskPriceDistance * desiredR;

            double currentTp = pos.TakeProfit ?? ctx.Tp2Price.Value;
            bool outward = pos.TradeType == TradeType.Buy ? newTp > currentTp : newTp < currentTp;
            if (!outward)
            {
                _bot.Print("[TTM] TP2 extension skipped=not outward");
                return;
            }

            if (ctx.LastExtendedTp2.HasValue && Math.Abs(ctx.LastExtendedTp2.Value - newTp) < _bot.Symbol.PipSize)
            {
                _bot.Print("[TTM] TP2 extension skipped=same target");
                return;
            }

            _bot.ModifyPosition(pos, pos.StopLoss, newTp);
            _bot.Print($"[EXIT] TP2 EXTENDED symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} oldTp={currentTp} newTp={newTp}");
            ctx.LastExtendedTp2 = newTp;
            ctx.Tp2ExtensionMultiplierApplied = desiredR / baseR;
            _bot.Print($"[TTM] TP2 extended from {currentTp} to {newTp}");
        }

    }
}
