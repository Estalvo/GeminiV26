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
using cAlgo.API.Internals;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Core.TradeManagement;
using GeminiV26.Data;
using GeminiV26.Data.Models;
using GeminiV26.EntryTypes.METAL; // XAU_InstrumentProfile + XAU_InstrumentMatrix (EntryTypes/Metal)
using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API.Indicators;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.XAUUSD
{
    public class XauExitManager : IExitManager
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;
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
        private readonly HashSet<long> _rehydratedResolverSkipLogged = new();


        private const double Tp1ProtectMinR = 0.8;
        private const int Tp1ProtectSwingLookback = 5;
        private const int Tp1SmartNoNewExtremeBars = 3;
        private const int Tp1SmartRangeLookback = 6;
        private const double Tp1SmartSpreadGuardFactor = 0.35;
        private const double Tp1SmartVolatilitySpikeFactor = 2.2;

        public XauExitManager(Robot bot)
        {
            _bot = bot;
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
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
            if (ctx == null)
                return;

            if (ctx.FinalDirection == TradeDirection.None)
            {
                if (!ctx.MissingDirLogged)
                {
                    GlobalLogger.Log(_bot, $"[DIR][ERROR] Missing FinalDirection posId={ctx.PositionId}");
                    ctx.MissingDirLogged = true;
                }

                return;
            }

            long key = Convert.ToInt64(ctx.PositionId);
            bool hasExisting = _contexts.TryGetValue(key, out var existingCtx) && existingCtx != null;
            bool suppressRehydrateRegistrationLog =
                ctx.RequiresRehydrateRecovery && hasExisting && existingCtx.RequiresRehydrateRecovery;
            bool suppressPostEntryRegistrationLog =
                hasExisting && existingCtx.PostEntryInitializationCompleted && ctx.PostEntryInitializationCompleted;

            _contexts[key] = ctx;

            if (!ctx.PostEntryInitializationCompleted)
                ctx.PostEntryInitializationCompleted = true;

            if (suppressRehydrateRegistrationLog || suppressPostEntryRegistrationLog)
                return;

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx));
        }

        // =====================================================
        // BAR-LEVEL EXIT (jelenleg csak early exit placeholder)
        // =====================================================
        private bool TryResolveExitSymbol(Position pos, out Symbol symbol, PositionContext ctx = null)
        {
            symbol = null;

            if (pos == null)
            {
                GlobalLogger.Log(_bot, "[RESOLVER][EXIT_SKIP] symbol=UNKNOWN positionId=0 reason=position_null");
                return false;
            }

            bool isRehydratedContext = ctx?.RequiresRehydrateRecovery == true;
            bool suppressRepeatedRehydrateResolverLog = false;
            if (isRehydratedContext)
            {
                long key = Convert.ToInt64(ctx.PositionId);
                suppressRepeatedRehydrateResolverLog = _rehydratedResolverSkipLogged.Contains(key);
                if (suppressRepeatedRehydrateResolverLog)
                {
                    symbol = _bot.Symbols.GetSymbol(pos.SymbolName);
                    if (symbol != null)
                    {
                        _rehydratedResolverSkipLogged.Remove(key);
                        ctx.RehydrateRecoveryCompleted = true;
                        GlobalLogger.Log(_bot, $"[RESOLVER][EXIT_RECOVER] symbol={pos.SymbolName} positionId={pos.Id} source=platform_symbols");
                        return true;
                    }

                    return false;
                }
            }

            if (_runtimeSymbols.TryGetSymbolMeta(pos.SymbolName, out symbol) && symbol != null)
            {
                if (isRehydratedContext)
                {
                    _rehydratedResolverSkipLogged.Remove(Convert.ToInt64(ctx.PositionId));
                    ctx.RehydrateRecoveryCompleted = true;
                }
                return true;
            }

            symbol = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (symbol != null)
            {
                if (isRehydratedContext)
                    ctx.RehydrateRecoveryCompleted = true;

                GlobalLogger.Log(_bot, $"[RESOLVER][EXIT_RECOVER] symbol={pos.SymbolName} positionId={pos.Id} source=platform_symbols");
                return true;
            }

            if (isRehydratedContext)
                _rehydratedResolverSkipLogged.Add(Convert.ToInt64(ctx.PositionId));

            GlobalLogger.Log(_bot, $"[RESOLVER][EXIT_SKIP] symbol={pos.SymbolName} positionId={pos.Id} reason=unresolved_runtime_symbol");
            return false;
        }

        private bool TryGetExitBars(Position pos, TimeFrame timeFrame, out Bars bars, PositionContext ctx = null)
        {
            bars = null;
            bool isRehydratedContext = ctx?.RequiresRehydrateRecovery == true;
            bool suppressRepeatedRehydrateResolverLog = false;
            if (isRehydratedContext)
            {
                long key = Convert.ToInt64(ctx.PositionId);
                suppressRepeatedRehydrateResolverLog = _rehydratedResolverSkipLogged.Contains(key);
            }

            if (pos == null || !_runtimeSymbols.TryGetBars(timeFrame, pos.SymbolName, out bars))
            {
                if (isRehydratedContext)
                    _rehydratedResolverSkipLogged.Add(Convert.ToInt64(ctx.PositionId));

                if (!suppressRepeatedRehydrateResolverLog)
                    GlobalLogger.Log(_bot, $"[RESOLVER][EXIT_SKIP] symbol={pos?.SymbolName ?? "UNKNOWN"} positionId={pos?.Id ?? 0} reason=unresolved_runtime_symbol");

                return false;
            }

            if (isRehydratedContext)
            {
                _rehydratedResolverSkipLogged.Remove(Convert.ToInt64(ctx.PositionId));
                ctx.RehydrateRecoveryCompleted = true;
            }

            return bars != null;
        }

        public void OnBar(Position pos)
        {
            if (pos == null)
            {
                GlobalLogger.Log(_bot, $"[EXIT][SKIP] reason=position_null symbol={_bot.SymbolName}");
                return;
            }

            if (!_contexts.TryGetValue(pos.Id, out var ctx))
            {
                GlobalLogger.Log(_bot, $"[EXIT][SKIP] reason=context_not_registered symbol={pos.SymbolName} positionId={pos.Id}");
                return;
            }

            // =====================================================
            // M5 bar counter (TVM / rescue / viability window)
            // =====================================================
            ctx.BarsSinceEntryM5++;

            if (TryResolveExitSymbol(pos, out var stateSymbol, ctx))
            {
                string stateFingerprint = $"{ctx.BarsSinceEntryM5}|{ctx.Tp1Hit}|{ctx.BeActivated}|{ctx.TrailingActivated}|{ctx.TrailSteps}";
                if (ctx.LastStateTraceBarIndex != ctx.BarsSinceEntryM5 || !string.Equals(ctx.LastStateTraceFingerprint, stateFingerprint, StringComparison.Ordinal))
                {
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildStateSnapshot(ctx, pos, stateSymbol), ctx, pos));
                    ctx.LastStateTraceBarIndex = ctx.BarsSinceEntryM5;
                    ctx.LastStateTraceFingerprint = stateFingerprint;
                }
            }

            if (ctx.Tp1Hit)
                TryEarlyExit(pos, ctx);
        }

        private void Debug(string msg)
        {
            if (DebugTp1)
                GlobalLogger.Log(_bot, msg);
        }

        // =====================================================
        // TICK-LEVEL EXIT
        // - TP1
        // - BE
        // - Trailing (TP1 után)
        // =====================================================
        public void OnTick()
        {
            var keys = new List<long>(_contexts.Keys);

            foreach (var key in keys)
            {
                if (!_contexts.TryGetValue(key, out var ctx) || ctx == null)
                    continue;

                if (ctx.FinalDirection == TradeDirection.None)
                    continue;

                var pos = FindPosition(ctx.PositionId);
                if (pos == null)
                {
                    if (ctx.Tp1Executed && !ctx.IsFullyClosing)
                        continue;

                    GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT][CLEANUP]\nreason=position_not_found", ctx));
                    _contexts.Remove(key);
                    _rehydratedResolverSkipLogged.Remove(key);
                    continue;
                }

                if (!pos.StopLoss.HasValue)
                    continue;

                if (!TryResolveExitSymbol(pos, out var sym, ctx))
                    continue;

                double currentPrice = ctx.FinalDirection == TradeDirection.Long
                    ? sym.Bid
                    : sym.Ask;
                // Keep MFE/MAE lifecycle tracking independent from TP1/TVM gating.
                GlobalLogger.Log(_bot, $"[ONTICK] time={_bot.Server.Time:HH:mm:ss.fff}");
                GlobalLogger.Log(_bot, $"[MFE_CALLSITE] symbol={_bot.SymbolName} price={currentPrice}");
                TradeLifecycleTracker.UpdateMfeMae(ctx, currentPrice);

                double rDist = GetRiskDistance(pos, ctx);

                if (rDist <= 0)
                {
                    rDist = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);

                    if (rDist > 0)
                        ctx.RiskPriceDistance = rDist;
                }

                if (rDist <= 0)
                    continue;

                // =========================
                // TP1 (TP1 előtt nincs trailing)
                // =========================
                if (!ctx.Tp1Hit)
                {
                    double tp1R = ResolveTp1R(ctx);

                    double tp1Price;

                    if (ctx.Tp1Price.HasValue && ctx.Tp1Price.Value > 0)
                    {
                        tp1Price = ctx.Tp1Price.Value;
                    }
                    else
                    {
                        tp1Price = IsLong(ctx)
                            ? pos.EntryPrice + rDist * tp1R
                            : pos.EntryPrice - rDist * tp1R;

                        ctx.Tp1Price = tp1Price;
                    }

                    if (ctx.Tp1R <= 0)
                        ctx.Tp1R = tp1R;

                    if (!ctx.InitialStopLossPrice.HasValue)
                    {
                        _bot.Print($"[WARN][SL_MISSING] symbol={_bot.SymbolName} fallback path active");
                    }

                    var slUsed = ctx.InitialStopLossPrice ?? ctx.LastStopLossPrice ?? pos.StopLoss;
                    _bot.Print($"[TP1_SOURCE] symbol={_bot.SymbolName} entry={ctx.EntryPrice} slUsed={slUsed} source={(ctx.InitialStopLossPrice.HasValue ? "initial" : "fallback")}");

                    // ------------------------------------------------
                    // TP1 HIT DETECTION
                    // ------------------------------------------------

                    bool hit =
                        IsLong(ctx)
                            ? sym.Bid >= tp1Price
                            : sym.Ask <= tp1Price;
                                        
                    if (!hit)
                    {
                        if (TryGetExitBars(pos, TimeFrame.Minute, out var m1, ctx) && m1.Count > 0)
                        {
                            var bar = m1.LastBar;

                            hit = IsLong(ctx)
                                ? bar.High >= tp1Price
                                : bar.Low <= tp1Price;
                        }
                    }

                    if (hit)
                    {
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT][TP1] symbol={pos.SymbolName} positionId={pos.Id} price={tp1Price:0.#####}", ctx, pos));
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[TP1][TOUCHED]\npos={pos.Id}\ntp1={tp1Price:0.#####}", ctx, pos));
                        ExecuteTp1(pos, ctx, rDist);
                        continue;
                    }

                    const int MinBarsBeforeTvm = 4;
                    
                    if (ctx.BarsSinceEntryM5 >= MinBarsBeforeTvm)
                    {
                        if (!TryGetExitBars(pos, TimeFrame.Minute5, out var m5, ctx) ||
                            !TryGetExitBars(pos, TimeFrame.Minute15, out var m15, ctx))
                        {
                            continue;
                        }

                        if (_tvm.ShouldEarlyExit(ctx, pos, m5, m15))
                        {
                            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT][DECISION]\nreason={ctx.DeadTradeReason}\ndetail=tvm_early_exit", ctx, pos));
                            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds("[EXIT SNAPSHOT]\n" +
                                $"symbol={pos.SymbolName}\n" +
                                $"positionId={pos.Id}\n" +
                                $"mfe={ctx.MfeR:0.##}\n" +
                                $"mae={ctx.MaeR:0.##}\n" +
                                $"tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}\n" +
                                $"barsOpen={ctx.BarsSinceEntryM5}\n" +
                                $"reason={ctx.DeadTradeReason}", ctx, pos));
                            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] reason={ctx.DeadTradeReason}", ctx, pos));
                            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[XAUUSD][TVM][EXIT] pos={pos.Id} reason={ctx.DeadTradeReason}", ctx, pos));

                            ctx.IsFullyClosing = true;
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

                            continue;
                        }
                    }

                    continue;
                }

                // =========================
                // TRAILING (TP1 után)
                // =========================
                UpdatePostTp1ProtectionState(ctx, currentPrice, rDist);

                if (CheckPostTp1ProfitProtection(ctx, currentPrice))
                    continue;

                var profile = TrailingProfiles.ResolveBySymbol(pos.SymbolName);
                var structure = _structureTracker.GetSnapshot();
                var decision = _trendTradeManager.Evaluate(pos, ctx, profile, structure);

                ctx.PostTp1TrendScore = decision.Score;
                ctx.PostTp1TrendState = decision.State.ToString();
                ctx.PostTp1TrailingMode = decision.TrailingMode.ToString();

                TryExtendTp2(pos, ctx, decision);
                _adaptiveTrailingEngine.Apply(pos, ctx, decision, structure, profile);
            }
        }

        // =====================================================
        // TP1 EXECUTION
        // =====================================================
        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            if (!TryResolveExitSymbol(pos, out var sym, ctx))
                return;

            // Partial close fraction: ctx-ből (executor beállította)
            double frac = ctx.Tp1CloseFraction;
            if (frac <= 0 || frac >= 1) frac = 0.40;

            double rawUnitsD = pos.VolumeInUnits * frac;
            long flooredUnits = (long)Math.Floor(rawUnitsD);
            long closeVolume = (long)sym.NormalizeVolumeInUnits(flooredUnits, RoundingMode.Down);

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] PARTIAL CLOSE check symbol={pos.SymbolName} positionId={pos.Id} volumeInUnits={pos.VolumeInUnits} frac={frac} rawUnits={rawUnitsD} flooredUnits={flooredUnits} closeVolume={closeVolume} min={sym.VolumeInUnitsMin} step={sym.VolumeInUnitsStep}", ctx, pos));
            
            if (closeVolume < sym.VolumeInUnitsMin)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] PARTIAL CLOSE skipped symbol={pos.SymbolName} positionId={pos.Id} reason=tp1_close_units_below_min rawUnits={rawUnitsD} flooredUnits={flooredUnits} closeVolume={closeVolume} min={sym.VolumeInUnitsMin} step={sym.VolumeInUnitsStep}", ctx, pos));
                return;
            }

            var closeResult = _bot.ClosePosition(pos, closeVolume);
            if (!closeResult.IsSuccessful)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] PARTIAL CLOSE failed symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(IsLong(ctx) ? sym.Bid : sym.Ask)} tp1={ctx.Tp1Price} rawUnits={rawUnitsD} flooredUnits={flooredUnits} closeVolume={closeVolume} min={sym.VolumeInUnitsMin} step={sym.VolumeInUnitsStep}", ctx, pos));
                GlobalLogger.Log(_bot, $"[TP1][FAIL] symbol={pos.SymbolName} positionId={pos.Id} volume={closeVolume} error={closeResult.Error}");
                return;
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] PARTIAL CLOSE executed symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(IsLong(ctx) ? sym.Bid : sym.Ask)} closedUnits={closeVolume}", ctx, pos));

            double executionPrice = IsLong(ctx) ? sym.Bid : sym.Ask;
            GlobalLogger.Log(_bot, $"[TP1][EXECUTED] volumeClosed={closeVolume} price={executionPrice}");

            // TP1 state (SSOT) – csak itt állítjuk
            ctx.Tp1Hit = true;
            GlobalLogger.Log(_bot, "[TP1] hit → MFE continues tracking");
            ctx.Tp1Executed = true;
            ctx.SetTp1HitBarIndex(ctx.BarsSinceEntryM5);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[TP1][EXECUTED]\ntp1={ctx.Tp1Price:0.#####}\nclosedUnits={closeVolume}", ctx, pos));

            // Remaining volume (analytics + rehydrate friendliness)
            ctx.Tp1ClosedVolumeInUnits = closeVolume;
            ctx.RemainingVolumeInUnits =
                Math.Max(0, pos.VolumeInUnits - closeVolume);

            // BE (profilból)
            var live = _bot.Positions.Find(pos.Label, pos.SymbolName, pos.TradeType);
            if (live == null)
            {
                GlobalLogger.Log(_bot, "[EXIT][POST_TP1_NO_POSITION]");
                _contexts.Remove(Convert.ToInt64(pos.Id));
                return;
            }

            if (live.VolumeInUnits < sym.VolumeInUnitsMin)
            {
                GlobalLogger.Log(_bot, "[EXIT][POST_TP1_MIN_VOLUME]");
                _contexts.Remove(Convert.ToInt64(pos.Id));
                return;
            }

            ApplyBreakEven(live, ctx, rDist);

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
            if (!TryResolveExitSymbol(pos, out var sym, ctx))
                return;

            double beOffsetR = _profile.BeOffsetR;
            if (beOffsetR <= 0) beOffsetR = 0.10;

            double bePrice =
                IsLong(ctx)
                    ? pos.EntryPrice + rDist * beOffsetR
                    : pos.EntryPrice - rDist * beOffsetR;

            ctx.BePrice = bePrice;
            double newSl = bePrice;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[BE] moved", ctx, pos));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] BE MOVE applied symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(IsLong(ctx) ? sym.Bid : sym.Ask)} be={bePrice}", ctx, pos));

            SafeModify(
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
            {
                GlobalLogger.Log(_bot, $"[EXIT][TRAIL][SKIP] reason=tp1_not_hit symbol={pos.SymbolName} positionId={pos.Id}");
                return;
            }

            if (!pos.StopLoss.HasValue)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TRAIL][SKIP] reason=no_stop_loss symbol={pos.SymbolName} positionId={pos.Id}");
                return;
            }

            if (!TryResolveExitSymbol(pos, out var sym, ctx))
            {
                if (!ctx.TrailResolverFailedLogged)
                {
                    GlobalLogger.Log(_bot, $"[EXIT][TRAIL][SKIP] reason=resolver_failed symbol={pos.SymbolName} positionId={pos.Id}");
                    ctx.TrailResolverFailedLogged = true;
                }
                return;
            }
            ctx.TrailResolverFailedLogged = false;

            // ===== ATR (előre inicializált indikátor!) =====
            double atrPrice = _atr.Result.LastValue;
            if (atrPrice <= 0)
            {
                if (!ctx.TrailAtrUnavailableLogged)
                {
                    GlobalLogger.Log(_bot, $"[EXIT][TRAIL][SKIP] reason=atr_unavailable symbol={pos.SymbolName} positionId={pos.Id}");
                    ctx.TrailAtrUnavailableLogged = true;
                }
                return;
            }
            ctx.TrailAtrUnavailableLogged = false;

            // ===== PROFIL SZERINTI ATR SZORZÓ =====
            double atrMult =
                ctx.TrailingMode == TrailingMode.Loose ? _profile.TrailAtrLoose :
                ctx.TrailingMode == TrailingMode.Normal ? _profile.TrailAtrNormal :
                                                          _profile.TrailAtrTight;

            double trailDistPrice = atrPrice * atrMult;

            // aktuális ár
            double priceNow =
                IsLong(ctx)
                    ? sym.Bid
                    : sym.Ask;

            // ===== PROGRESS SZŰRŐ (ATR-ALAPÚ, STABIL) =====
            double progressDist =
                IsLong(ctx)
                    ? priceNow - pos.StopLoss.Value
                    : pos.StopLoss.Value - priceNow;

            // legalább 0.25 ATR előny kell
            if (progressDist < atrPrice * 0.25)
            {
                if (!ctx.TrailLowProgressLogged)
                {
                    GlobalLogger.Log(_bot, $"[EXIT][TRAIL][SKIP] reason=low_progress symbol={pos.SymbolName} positionId={pos.Id}");
                    ctx.TrailLowProgressLogged = true;
                }
                return;
            }
            ctx.TrailLowProgressLogged = false;

            // ===== ÚJ SL =====
            double newSl =
                IsLong(ctx)
                    ? priceNow - trailDistPrice
                    : priceNow + trailDistPrice;

            // ===== MINIMÁLIS JAVULÁS (PIPS) =====
            double improvePips =
                IsLong(ctx)
                    ? (newSl - pos.StopLoss.Value) / sym.PipSize
                    : (pos.StopLoss.Value - newSl) / sym.PipSize;

            if (improvePips < _profile.MinTrailImprovePips)
            {
                if (!ctx.TrailNoImprovementLogged)
                {
                    GlobalLogger.Log(_bot, $"[EXIT][TRAIL][SKIP] reason=no_improvement symbol={pos.SymbolName} positionId={pos.Id}");
                    ctx.TrailNoImprovementLogged = true;
                }
                return;
            }
            ctx.TrailNoImprovementLogged = false;

            SafeModify(
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

            int confidence = ctx.AdjustedRiskConfidence;

            if (confidence <= 0)
            {
                confidence = ctx.FinalConfidence;
                GlobalLogger.Log(_bot, "[CONF][EXIT_FALLBACK] using FinalConfidence");
            }

            // Profil bucket
            if (confidence >= 85) return _profile.Tp1R_High;
            if (confidence >= 70) return _profile.Tp1R_Normal;
            return _profile.Tp1R_Low;
        }

        private double GetRiskDistance(Position pos, PositionContext ctx)
        {
            // 1) Legjobb: immutable entrykori SL snapshot
            if (ctx.InitialStopLossPrice.HasValue)
            {
                return Math.Abs(ctx.EntryPrice - ctx.InitialStopLossPrice.Value);
            }

            // 2) ctx.RiskPriceDistance (belépéskori SSOT)
            if (ctx.RiskPriceDistance > 0)
                return ctx.RiskPriceDistance;

            // 3) Ha nincs: próbáljuk a “kanonikus” SL snapshotot (rehydrate / executor töltheti)
            if (ctx.LastStopLossPrice.HasValue && ctx.LastStopLossPrice.Value > 0 && ctx.EntryPrice > 0)
            {
                double d = Math.Abs(ctx.EntryPrice - ctx.LastStopLossPrice.Value);
                if (d > 0) return d;   // <-- marad double!
            }

            // 4) Végső fallback (nem ideális): entryPrice vs aktuális SL
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
                    Bot = bot,
                    TempId = pos.Comment ?? string.Empty,
                    EntryPrice = pos.EntryPrice,

                    EntryVolumeInUnits = pos.VolumeInUnits,
                    RemainingVolumeInUnits = pos.VolumeInUnits,
                    InitialVolumeInUnits = pos.VolumeInUnits,

                    // Neutral confidence placeholders (same rehydrate convention as RehydrateService).
                    EntryScore = 50,
                    LogicConfidence = 50,

                    // SSOT snapshot
                    RiskPriceDistance = rDist,
                    PipSize = bot.Symbol.PipSize,
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
                GlobalLogger.Log(_bot, $"[XAU REHYDRATE] pos={pos.Id}");
            }
        }

        private void UpdatePostTp1ProtectionState(PositionContext ctx, double currentPrice, double rDist)
        {
            if (ctx == null || !ctx.Tp1Hit || rDist <= 0)
                return;

            double currentR = (IsLong(ctx) ? (currentPrice - ctx.EntryPrice) : (ctx.EntryPrice - currentPrice)) / rDist;
            if (currentR > ctx.PostTp1MaxR)
            {
                ctx.PostTp1MaxR = currentR;
                ctx.PostTp1MaxPrice = currentPrice;
            }
        }

        public bool CheckPostTp1ProfitProtection(PositionContext ctx, double currentPrice)
        {
            var pos = ctx == null ? null : FindPosition(ctx.PositionId);
            return CheckPostTp1ProfitProtection(pos, ctx, currentPrice);
        }

        private bool CheckPostTp1ProfitProtection(Position pos, PositionContext ctx, double currentPrice)
        {
            if (ctx == null || pos == null || !ctx.Tp1Hit)
                return false;

            if (ctx.IsFullyClosing)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR=0 reason=TREND_COLLAPSE noAction=already_closing tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            ctx.RemainingVolumeInUnits = pos.VolumeInUnits;
            bool activation = ctx.Tp1ClosedVolumeInUnits > 0 && ctx.RemainingVolumeInUnits > 0;
            if (!activation)
                return false;

            double rDist = GetRiskDistance(pos, ctx);
            if (rDist <= 0)
                return false;

            double currentR = (IsLong(ctx) ? (currentPrice - ctx.EntryPrice) : (ctx.EntryPrice - currentPrice)) / rDist;
            if (currentR < Tp1ProtectMinR)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=insufficient_profit_threshold swingBroken=false noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening=false tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            if (!TryGetExitBars(pos, TimeFrame.Minute, out var m1, ctx) || m1 == null || m1.Count < 12)
                return false;

            int last = m1.Count - 2;
            if (last < 8)
                return false;

            var livePos = FindPosition(ctx.PositionId);
            if (livePos == null)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=position_not_found swingBroken=false noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening=false tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            double tickSize = _bot.Symbols.GetSymbol(pos.SymbolName)?.TickSize ?? 1e-5;
            double eps = Math.Max(1e-8, tickSize * 0.5);

            var structure = _structureTracker.GetSnapshot();
            double? structuralSwingRef = IsLong(ctx)
                ? structure?.LastSwingLow?.Price
                : structure?.LastSwingHigh?.Price;

            if (!structuralSwingRef.HasValue || structuralSwingRef.Value <= 0)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=missing_safe_swing_reference swingBroken=false noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening=false tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            bool swingBroken = IsLong(ctx)
                ? currentPrice < (structuralSwingRef.Value - eps)
                : currentPrice > (structuralSwingRef.Value + eps);
            if (!swingBroken)
                return false;

            int recentStart = Math.Max(1, last - Tp1SmartNoNewExtremeBars + 1);
            int priorEnd = recentStart - 1;
            int priorStart = Math.Max(1, priorEnd - Tp1SmartNoNewExtremeBars + 1);
            if (priorEnd <= 0 || priorEnd - priorStart + 1 < Tp1SmartNoNewExtremeBars)
                return false;

            double recentHigh = double.MinValue;
            double recentLow = double.MaxValue;
            double priorHigh = double.MinValue;
            double priorLow = double.MaxValue;
            for (int i = recentStart; i <= last; i++)
            {
                if (m1.HighPrices[i] > recentHigh) recentHigh = m1.HighPrices[i];
                if (m1.LowPrices[i] < recentLow) recentLow = m1.LowPrices[i];
            }

            for (int i = priorStart; i <= priorEnd; i++)
            {
                if (m1.HighPrices[i] > priorHigh) priorHigh = m1.HighPrices[i];
                if (m1.LowPrices[i] < priorLow) priorLow = m1.LowPrices[i];
            }

            bool noNewExtreme = IsLong(ctx)
                ? recentHigh <= (priorHigh + eps)
                : recentLow >= (priorLow - eps);
            if (!noNewExtreme)
                return false;

            double currRange = Math.Max(m1.HighPrices[last] - m1.LowPrices[last], eps);
            double prevRange = Math.Max(m1.HighPrices[last - 1] - m1.LowPrices[last - 1], eps);
            double prev2Range = Math.Max(m1.HighPrices[last - 2] - m1.LowPrices[last - 2], eps);

            double currOpen = m1.OpenPrices[last];
            double currClose = m1.ClosePrices[last];
            double prevOpen = m1.OpenPrices[last - 1];
            double prevClose = m1.ClosePrices[last - 1];
            double prev2Open = m1.OpenPrices[last - 2];
            double prev2Close = m1.ClosePrices[last - 2];

            double currBody = Math.Abs(currClose - currOpen);
            double prevBody = Math.Abs(prevClose - prevOpen);
            double prev2Body = Math.Abs(prev2Close - prev2Open);

            bool transitionQualityWorse = currBody < prevBody;
            bool impulseWeakening = currBody < prevBody && prevBody < prev2Body && currRange < prevRange;

            int rangeStart = Math.Max(1, last - Tp1SmartRangeLookback + 1);
            int mid = Math.Max(rangeStart + 1, rangeStart + (last - rangeStart + 1) / 2);
            double recentRangeSum = 0;
            int recentRangeCount = 0;
            double priorRangeSum = 0;
            int priorRangeCount = 0;
            for (int i = rangeStart; i <= last; i++)
            {
                double range = Math.Max(m1.HighPrices[i] - m1.LowPrices[i], eps);
                if (i >= mid)
                {
                    recentRangeSum += range;
                    recentRangeCount++;
                }
                else
                {
                    priorRangeSum += range;
                    priorRangeCount++;
                }
            }

            double recentAvgRange = recentRangeCount > 0 ? recentRangeSum / recentRangeCount : currRange;
            double priorAvgRange = priorRangeCount > 0 ? priorRangeSum / priorRangeCount : Math.Max(prevRange, prev2Range);
            bool compressionStall = priorAvgRange > eps && recentAvgRange <= priorAvgRange * 0.70;

            int momentumSignals = 0;
            if (transitionQualityWorse) momentumSignals++;
            if (impulseWeakening) momentumSignals++;
            if (compressionStall) momentumSignals++;

            if (momentumSignals < 2)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=no_collapse_confirmation swingBroken={swingBroken.ToString().ToLowerInvariant()} noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening=false tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            bool momentumWeakening = momentumSignals >= 2;

            if (!TryResolveExitSymbol(pos, out var liveSymbol, ctx))
                return false;

            double currentSpread = Math.Abs(liveSymbol.Ask - liveSymbol.Bid);
            if (currentSpread > recentAvgRange * Tp1SmartSpreadGuardFactor)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=spread_spike_guard swingBroken={swingBroken.ToString().ToLowerInvariant()} noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening={momentumWeakening.ToString().ToLowerInvariant()} tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            bool volatilitySpike = currRange > Math.Max(priorAvgRange, eps) * Tp1SmartVolatilitySpikeFactor;
            if (volatilitySpike && momentumSignals < 2)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=volatility_spike_guard swingBroken={swingBroken.ToString().ToLowerInvariant()} noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening={momentumWeakening.ToString().ToLowerInvariant()} tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            bool trailingConflict = livePos.StopLoss.HasValue &&
                (IsLong(ctx)
                    ? currentPrice < (livePos.StopLoss.Value - eps)
                    : currentPrice > (livePos.StopLoss.Value + eps));

            if (trailingConflict)
                return false;

            int barsSinceTp1 = ctx.GetTp1HitBarIndex() >= 0
                ? Math.Max(0, ctx.BarsSinceEntryM5 - ctx.GetTp1HitBarIndex())
                : 0;

            ctx.Tp1ProtectExitHit = true;
            ctx.Tp1ProtectExitR = currentR;
            ctx.Tp1ProtectScoreAtExit = momentumSignals;
            ctx.Tp1ProtectMode = "TP1_SMART";
            ctx.PostTp1GivebackR = Math.Max(0, ctx.PostTp1MaxR - currentR);
            ctx.SetTp1SmartExitHit(true);
            ctx.SetTp1SmartExitType("TP1_SMART");
            ctx.SetTp1SmartExitReason("TREND_COLLAPSE");
            ctx.SetTp1SmartExitR(currentR);
            ctx.SetTp1SmartBarsSinceTp1(barsSinceTp1);
            ctx.IsFullyClosing = true;

            GlobalLogger.Log(_bot,
                $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE swingBroken={swingBroken.ToString().ToLowerInvariant()} noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening={momentumWeakening.ToString().ToLowerInvariant()} tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()} exitType=TP1_SMART exitReason=TREND_COLLAPSE exitR={currentR:0.###} barsSinceTP1={barsSinceTp1}");

            _bot.ClosePosition(livePos);
            return true;
        }


        private void TryExtendTp2(Position pos, PositionContext ctx, TrendDecision decision)
        {
            if (!decision.AllowTp2Extension)
            {
                GlobalLogger.Log(_bot, $"[TP2][SKIP] reason=extension_disabled symbol={pos.SymbolName} positionId={pos.Id}");
                return;
            }

            if (!ctx.Tp2Price.HasValue)
            {
                GlobalLogger.Log(_bot, $"[TP2][SKIP] reason=tp2_missing symbol={pos.SymbolName} positionId={pos.Id}");
                return;
            }

            if (!ctx.Tp2Price.Value.Equals(pos.TakeProfit ?? ctx.Tp2Price.Value))
                return;

            double? currentPrice = null;
            if (TryResolveExitSymbol(pos, out var sym, ctx))
                currentPrice = IsLong(ctx) ? sym.Bid : sym.Ask;

            double baseR = ctx.Tp2R > 0 ? ctx.Tp2R : 1.0;
            double desiredR = baseR * decision.Tp2ExtensionMultiplier;
            double currentR = ctx.Tp2ExtensionMultiplierApplied > 0 ? baseR * ctx.Tp2ExtensionMultiplierApplied : baseR;

            if (desiredR <= currentR + 0.0001)
                return;

            double newTp = IsLong(ctx)
                ? pos.EntryPrice + ctx.RiskPriceDistance * desiredR
                : pos.EntryPrice - ctx.RiskPriceDistance * desiredR;

            double currentTp = pos.TakeProfit ?? ctx.Tp2Price.Value;
            bool outward = IsLong(ctx) ? newTp > currentTp : newTp < currentTp;
            if (!outward)
            {
                GlobalLogger.Log(_bot, $"[TP2][SKIP] reason=not_outward symbol={pos.SymbolName} positionId={pos.Id}");
                return;
            }

            if (ctx.LastExtendedTp2.HasValue && Math.Abs(ctx.LastExtendedTp2.Value - newTp) < _bot.Symbol.PipSize)
                return;

            SafeModify(pos, pos.StopLoss, newTp);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT] TP2 EXTENDED symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(IsLong(ctx) ? sym.Bid : sym.Ask)} oldTp={currentTp} newTp={newTp}", ctx, pos));
            ctx.LastExtendedTp2 = newTp;
            ctx.Tp2ExtensionMultiplierApplied = desiredR / baseR;
        }


        private Position FindPosition(long positionId)
        {
            foreach (var position in _bot.Positions)
            {
                if (Convert.ToInt64(position.Id) == positionId)
                    return position;
            }

            return null;
        }

        private void SafeModify(Position position, double? sl, double? tp)
        {
            if (position == null)
                return;

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[MODIFY][REQUEST]\nsl={sl}\ntp={tp}", position.Id, null, position.SymbolName));
            var livePos = FindPosition(Convert.ToInt64(position.Id));
            if (livePos == null)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[MODIFY][FAIL]\nsl={sl}\ntp={tp}\nerror=POSITION_NOT_FOUND", position.Id, null, position.SymbolName));
                GlobalLogger.Log(_bot, $"[SAFE_MODIFY][SKIP_NO_POSITION] pos={position.Id}");
                return;
            }

            var result = _bot.ModifyPosition(livePos, sl, tp);
            if (!result.IsSuccessful)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[MODIFY][FAIL]\nsl={sl}\ntp={tp}\nerror={result.Error}", position.Id, null, position.SymbolName));
                GlobalLogger.Log(_bot, $"[SAFE_MODIFY][FAIL] {position.Id} error={result.Error}");
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[BE][FAIL]\nsl={sl}\ntp={tp}\nerror={result.Error}", position.Id, null, position.SymbolName));
                return;
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[MODIFY][SUCCESS]\nsl={sl}\ntp={tp}", position.Id, null, position.SymbolName));
        }

        private static bool IsLong(PositionContext ctx)
        {
            return ctx?.FinalDirection == TradeDirection.Long;
        }

    }
}
