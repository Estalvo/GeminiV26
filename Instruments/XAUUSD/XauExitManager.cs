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

        // PositionId → Context
        private readonly Dictionary<long, PositionContext> _contexts = new();

        public XauExitManager(Robot bot)
        {
            _bot = bot;
            _eventLogger = new EventLogger(bot.SymbolName);

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
            foreach (var pos in _bot.Positions.ToList())
            {
                if (!pos.SymbolName.Contains("XAU"))
                    continue;

                if (!pos.StopLoss.HasValue)
                    continue;

                if (!_contexts.TryGetValue(pos.Id, out var ctx))
                {
                    Debug($"[XAU EXIT DBG] NO CTX for pos={pos.Id}");
                    continue;
                }

                var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
                if (sym == null)
                    continue;

                // -------------------------------------------------
                // SSOT: TP1Price preferált, ha executor már kiszámolta.
                // Ha nincs TP1Price, akkor számolunk:
                // - prefer: ctx.RiskPriceDistance
                // - fallback: pos.EntryPrice vs pos.StopLoss (végső)
                // -------------------------------------------------
                double? tp1PriceMaybe = ctx.Tp1Price;

                double rDist = GetRiskDistance(pos, ctx);
                if (rDist <= 0)
                    continue;
                
                // =========================
                // TP1 (TP1 előtt nincs trailing)
                // =========================
                if (!ctx.Tp1Hit)
                {
                    double tp1Price;

                    if (tp1PriceMaybe.HasValue && tp1PriceMaybe.Value > 0)
                    {
                        tp1Price = tp1PriceMaybe.Value;
                    }
                    else
                    {
                        // profil bucket → ha ctx.Tp1R nincs rendesen kitöltve, pótoljuk
                        double tp1R = ResolveTp1R(ctx);
                        tp1Price = pos.TradeType == TradeType.Buy
                            ? pos.EntryPrice + rDist * tp1R
                            : pos.EntryPrice - rDist * tp1R;

                        // opcionális: ctx.Tp1Price beégetése, hogy stabil legyen később is
                        ctx.Tp1Price = tp1Price;
                        if (ctx.Tp1R <= 0) ctx.Tp1R = tp1R;
                    }

                    double priceNow =
                        pos.TradeType == TradeType.Buy
                            ? sym.Bid
                            : sym.Ask;

                    Debug(
                        $"[XAU TP1 DBG] pos={pos.Id} dir={pos.TradeType} " +
                        $"entry={pos.EntryPrice:F2} SL={pos.StopLoss.Value:F2} " +
                        $"rDist={rDist:F2} TP1@={tp1Price:F2} price={priceNow:F2}"
                    );

                    bool hit =
                        pos.TradeType == TradeType.Buy
                            ? sym.Bid >= tp1Price
                            : sym.Ask <= tp1Price;

                    if (hit)
                    {
                        ExecuteTp1(pos, ctx, rDist);
                        continue; // TP1 után trailing a következő tickben indul
                    }

                    continue; // TP1 előtt trailing tilos
                }

                // =========================
                // TRAILING (TP1 után)
                // =========================
                ApplyTrailing(pos, ctx);
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

            double closeVolume =
                sym.NormalizeVolumeInUnits(
                    pos.VolumeInUnits * frac,
                    RoundingMode.Down
                );

            if (closeVolume < sym.VolumeInUnitsMin)
                return;

            _bot.ClosePosition(pos, closeVolume);

            // TP1 state (SSOT) – csak itt állítjuk
            ctx.Tp1Hit = true;

            // Remaining volume (analytics + rehydrate friendliness)
            ctx.RemainingVolumeInUnits =
                Math.Max(0, ctx.EntryVolumeInUnits - closeVolume);

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
                if (!pos.SymbolName.Contains("XAU"))
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

                // TP1R becslés rehydrate-nél (nincs FC, ezért normal bucket)
                ctx.Tp1R = _profile.Tp1R_Normal;
                ctx.Tp1CloseFraction = 0.40;

                RegisterContext(ctx);
                bot.Print($"[XAU REHYDRATE] pos={pos.Id}");
            }
        }
    }
}
