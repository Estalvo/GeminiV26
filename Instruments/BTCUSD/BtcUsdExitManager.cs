using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using cAlgo.API.Indicators;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Core.TradeManagement;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.BTCUSD
{
    /// <summary>
    /// BTCUSD ExitManager
    /// - Régi BTC TP1 logika megtartva (M1 wick touch)
    /// - TP1 előtt nincs trailing
    /// - TP1 után: BE + TTM + adaptive trailing + optional TP2 extension
    /// </summary>
    public class BtcUsdExitManager : IExitManager
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;

        // PositionId -> context
        private readonly Dictionary<long, PositionContext> _contexts = new();
        private readonly HashSet<long> _rehydratedResolverSkipLogged = new();

        private readonly TradeViabilityMonitor _tvm;
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;

        // ===== TP1 / BE =====
        private const double BeOffsetR = 0.05;

        private const double Tp1ProtectMinR = 0.8;
        private const int Tp1ProtectSwingLookback = 5;
        private const int Tp1SmartNoNewExtremeBars = 3;
        private const int Tp1SmartRangeLookback = 6;
        private const double Tp1SmartSpreadGuardFactor = 0.35;
        private const double Tp1SmartVolatilitySpikeFactor = 2.2;

        // ===== Legacy BTC trailing fallback =====
        private const double MinTrailImprovePips = 10;
        private const double TrailTight = 1.2;
        private const double TrailNormal = 1.8;
        private const double TrailLoose = 2.5;

        private const int AtrPeriod = 14;
        private readonly AverageTrueRange _atrM5;

        public BtcUsdExitManager(Robot bot)
        {
            _bot = bot;
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
            _tvm = new TradeViabilityMonitor(bot);
            _trendTradeManager = new TrendTradeManager(_bot, _bot.Bars);
            _adaptiveTrailingEngine = new AdaptiveTrailingEngine(_bot);
            _structureTracker = new StructureTracker(_bot, _bot.Bars);

            _atrM5 = _bot.Indicators.AverageTrueRange(
                AtrPeriod,
                MovingAverageType.Exponential
            );
        }

        // =========================================================
        // CONTEXT REGISZTRÁCIÓ
        // =========================================================
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

        // =========================================================
        // BAR-LEVEL EXIT MANAGEMENT
        // =========================================================
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

        public void OnBar(Position position)
        {
            if (position == null)
            {
                GlobalLogger.Log(_bot, $"[EXIT][SKIP] reason=position_null symbol={_bot.SymbolName}");
                return;
            }

            long key = Convert.ToInt64(position.Id);

            if (!_contexts.TryGetValue(key, out var ctx) || ctx == null)
            {
                GlobalLogger.Log(_bot, $"[EXIT][SKIP] reason=context_not_registered symbol={position.SymbolName} positionId={position.Id}");
                return;
            }

            ctx.BarsSinceEntryM5++;

            if (TryResolveExitSymbol(position, out var stateSymbol, ctx))
            {
                string stateFingerprint = $"{ctx.BarsSinceEntryM5}|{ctx.Tp1Hit}|{ctx.BeActivated}|{ctx.TrailingActivated}|{ctx.TrailSteps}";
                if (ctx.LastStateTraceBarIndex != ctx.BarsSinceEntryM5 || !string.Equals(ctx.LastStateTraceFingerprint, stateFingerprint, StringComparison.Ordinal))
                {
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildStateSnapshot(ctx, position, stateSymbol), ctx, position));
                    ctx.LastStateTraceBarIndex = ctx.BarsSinceEntryM5;
                    ctx.LastStateTraceFingerprint = stateFingerprint;
                }
            }
        }

        // =========================================================
        // TICK-LEVEL EXIT MANAGEMENT
        // =========================================================
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
                // TP1 (legacy crypto-touch: M1 wick)
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

                    bool reached =
                        IsLong(ctx)
                            ? sym.Bid >= tp1Price
                            : sym.Ask <= tp1Price;

                    if (!reached)
                    {
                        if (TryGetExitBars(pos, TimeFrame.Minute, out var m1, ctx) && m1.Count > 0)
                        {
                            var m1Bar = m1.LastBar;

                            reached = IsLong(ctx)
                                ? m1Bar.High >= tp1Price
                                : m1Bar.Low <= tp1Price;
                        }
                    }

                    if (reached)
                    {
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT][TP1] symbol={pos.SymbolName} positionId={pos.Id} price={tp1Price:0.#####}", ctx, pos));
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[TP1][TOUCHED]\npos={pos.Id}\ntp1={tp1Price:0.#####}", ctx, pos));
                        ExecuteTp1(pos, ctx, rDist);

                        // Legacy viselkedés:
                        // TP1 után azonnal kilépünk ebből a tickből,
                        // hogy ne legyen state-ütközés partial close / modify miatt.
                        continue;
                    }

                    // =========================
                    // TVM – Early Exit (TP1 előtt)
                    // =========================
                    {
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
                                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                                    $"[TVM EXIT] {pos.SymbolName} pos={pos.Id} " +
                                    $"reason={ctx.DeadTradeReason} " +
                                    $"MFE_R={ctx.MfeR:0.00} MAE_R={ctx.MaeR:0.00} " +
                                    $"barsM5={ctx.BarsSinceEntryM5}", ctx, pos));

                                ctx.IsFullyClosing = true;
                                _bot.ClosePosition(pos);
                                _contexts.Remove(key);
                    _rehydratedResolverSkipLogged.Remove(key);
                                continue;
                            }
                        }
                    }

                    // TP1 előtt trailing TILOS
                    continue;
                }

                // =========================
                // TP1 után: TTM + adaptive trailing
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

        // =========================================================
        // Manage() megmarad, de exit logika OnTick-ben fut
        // =========================================================
        public void Manage(Position pos)
        {
        }

        // =========================================================
        // TP1 EXECUTION (legacy BTC flow)
        // =========================================================
        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            if (!TryResolveExitSymbol(pos, out var sym, ctx))
                return;

            // 1) Partial close (crypto-safe)
            double frac = ctx.Tp1CloseFraction;
            if (frac <= 0 || frac >= 1)
                frac = 0.5;

            long closeUnits = (long)sym.NormalizeVolumeInUnits(
                (long)Math.Floor(pos.VolumeInUnits * frac),
                RoundingMode.Down
            );

            long minUnits = (long)sym.VolumeInUnitsMin;

            if (closeUnits < minUnits)
            {
                GlobalLogger.Log(_bot, $"[TP1][SKIP] reason=tp1_close_units_below_min symbol={pos.SymbolName} positionId={pos.Id} volume={closeUnits}");
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                    $"[BTCUSD][TP1][SKIP] partial close below min " +
                    $"pos={pos.Id} requested={closeUnits} min={minUnits}", ctx, pos));
                return;
            }

            if (closeUnits >= pos.VolumeInUnits)
            {
                closeUnits = (long)sym.NormalizeVolumeInUnits(
                    (long)Math.Floor(pos.VolumeInUnits - minUnits),
                    RoundingMode.Down
                );
            }

            if (closeUnits < minUnits)
            {
                GlobalLogger.Log(_bot, $"[TP1][SKIP] reason=tp1_close_units_below_min symbol={pos.SymbolName} positionId={pos.Id} volume={closeUnits}");
                GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                    $"[BTCUSD][TP1][SKIP] adjusted partial close below min " +
                    $"pos={pos.Id} adjusted={closeUnits} min={minUnits}", ctx, pos));
                return;
            }

            var closeResult = _bot.ClosePosition(pos, closeUnits);
            if (!closeResult.IsSuccessful)
            {
                GlobalLogger.Log(_bot, $"[TP1][FAIL] symbol={pos.SymbolName} positionId={pos.Id} volume={closeUnits} error={closeResult.Error}");
                return;
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[EXIT] PARTIAL CLOSE executed symbol={pos.SymbolName} positionId={pos.Id} " +
                $"direction={pos.TradeType} currentPrice={(IsLong(ctx) ? sym.Bid : sym.Ask)} " +
                $"closedUnits={closeUnits}", ctx, pos));

            double executionPrice = IsLong(ctx) ? sym.Bid : sym.Ask;
            GlobalLogger.Log(_bot, $"[TP1][EXECUTED] volumeClosed={closeUnits} price={executionPrice}");

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = Math.Max(0, pos.VolumeInUnits - closeUnits);

            // 2) TP1 state
            ctx.Tp1Hit = true;
            GlobalLogger.Log(_bot, "[TP1] hit → MFE continues tracking");
            ctx.Tp1Executed = true;
            ctx.Tp1HitBarIndex = ctx.BarsSinceEntryM5;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[TP1][EXECUTED]\ntp1={ctx.Tp1Price:0.#####}\nclosedUnits={closeUnits}", ctx, pos));

            // 3) BE / protective SL after TP1
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
        }

        // =========================================================
        // Legacy fallback trailing
        // Megtartva biztonsági tartalékként, ha később kell
        // =========================================================
        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            if (!pos.StopLoss.HasValue)
                return;

            if (ctx.TrailingMode == TrailingMode.None)
                ctx.TrailingMode = TrailingMode.Normal;

            double atr = _atrM5.Result.LastValue;
            if (atr <= 0)
                return;

            double mult = GetTrailMultiplier(ctx.TrailingMode);
            double trailDist = atr * mult;

            double desiredSl =
                IsLong(ctx)
                    ? _bot.Symbol.Bid - trailDist
                    : _bot.Symbol.Ask + trailDist;

            if (ctx.BePrice > 0)
            {
                desiredSl = IsLong(ctx)
                    ? Math.Max(desiredSl, ctx.BePrice)
                    : Math.Min(desiredSl, ctx.BePrice);
            }

            double minImprovePip = _bot.Symbol.PipSize * MinTrailImprovePips;
            double minImproveAtr = atr * 0.15;
            double minImprove = Math.Max(minImprovePip, minImproveAtr);

            bool improve =
                (IsLong(ctx) && desiredSl > pos.StopLoss.Value + minImprove) ||
                (!IsLong(ctx) && desiredSl < pos.StopLoss.Value - minImprove);

            if (!improve)
                return;

            SafeModify(pos, desiredSl, pos.TakeProfit);
        }

        private void ApplyBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            double bePrice =
                IsLong(ctx)
                    ? pos.EntryPrice + rDist * BeOffsetR
                    : pos.EntryPrice - rDist * BeOffsetR;

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[BE][REQUEST]\nbePrice={bePrice:0.#####}\ntp={pos.TakeProfit}", ctx, pos));
            SafeModify(pos, bePrice, pos.TakeProfit);

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;
            ctx.BeActivated = true;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[BE][SUCCESS]\nbePrice={bePrice:0.#####}\ntp={pos.TakeProfit}", ctx, pos));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[BE] moved", ctx, pos));

            if (ctx.TrailingMode == TrailingMode.None)
                ctx.TrailingMode = TrailingMode.Normal;
        }

        private double ResolveTp1R(PositionContext ctx)
        {
            if (ctx.Tp1R > 0)
                return ctx.Tp1R;

            int confidence = ctx.AdjustedRiskConfidence;

            if (confidence <= 0)
            {
                confidence = ctx.FinalConfidence;
                GlobalLogger.Log(_bot, "[CONF][EXIT_FALLBACK] using FinalConfidence");
            }

            if (confidence >= 85)
                return 0.40;

            if (confidence >= 70)
                return 0.50;

            return 0.60;
        }

        private double GetRiskDistance(Position pos, PositionContext ctx)
        {
            if (ctx.InitialStopLossPrice.HasValue)
            {
                return Math.Abs(ctx.EntryPrice - ctx.InitialStopLossPrice.Value);
            }

            if (ctx.RiskPriceDistance > 0)
                return ctx.RiskPriceDistance;

            if (ctx.LastStopLossPrice.HasValue && ctx.LastStopLossPrice.Value > 0 && ctx.EntryPrice > 0)
            {
                double d = Math.Abs(ctx.EntryPrice - ctx.LastStopLossPrice.Value);
                if (d > 0)
                    return d;
            }

            double d2 = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);
            return d2 > 0 ? d2 : 0;
        }

        private static double GetTrailMultiplier(TrailingMode mode)
        {
            return mode switch
            {
                TrailingMode.Tight => TrailTight,
                TrailingMode.Normal => TrailNormal,
                TrailingMode.Loose => TrailLoose,
                _ => TrailNormal
            };
        }

        private void UpdatePostTp1ProtectionState(PositionContext ctx, double currentPrice, double rDist)
        {
            if (ctx == null || !ctx.Tp1Hit || rDist <= 0)
                return;

            double currentR = Math.Abs(currentPrice - ctx.EntryPrice) / rDist;
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

            double currentR = Math.Abs(currentPrice - ctx.EntryPrice) / rDist;
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

            int swingStart = Math.Max(1, last - Tp1ProtectSwingLookback + 1);
            if (last - swingStart + 1 < 3)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=missing_safe_swing_reference swingBroken=false noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening=false tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            double lastSwingLow = double.MaxValue;
            double lastSwingHigh = double.MinValue;
            for (int i = swingStart; i <= last; i++)
            {
                if (m1.LowPrices[i] < lastSwingLow) lastSwingLow = m1.LowPrices[i];
                if (m1.HighPrices[i] > lastSwingHigh) lastSwingHigh = m1.HighPrices[i];
            }

            bool swingBroken = IsLong(ctx)
                ? currentPrice < (lastSwingLow - eps)
                : currentPrice > (lastSwingHigh + eps);
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

            if (momentumSignals == 0)
            {
                GlobalLogger.Log(_bot, $"[EXIT][TP1_SMART_EXIT] symbol={pos.SymbolName} side={(IsLong(ctx) ? "LONG" : "SHORT")} entry={ctx.EntryPrice:0.#####} currentPrice={currentPrice:0.#####} currentR={currentR:0.###} reason=TREND_COLLAPSE noAction=no_collapse_confirmation swingBroken={swingBroken.ToString().ToLowerInvariant()} noNewExtremeBars={Tp1SmartNoNewExtremeBars} momentumWeakening=false tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}");
                return false;
            }

            bool momentumWeakening = momentumSignals >= 1;

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

            int barsSinceTp1 = ctx.Tp1HitBarIndex >= 0
                ? Math.Max(0, ctx.BarsSinceEntryM5 - ctx.Tp1HitBarIndex)
                : 0;

            ctx.Tp1ProtectExitHit = true;
            ctx.Tp1ProtectExitR = currentR;
            ctx.Tp1ProtectScoreAtExit = momentumSignals;
            ctx.Tp1ProtectMode = "TP1_SMART";
            ctx.PostTp1GivebackR = Math.Max(0, ctx.PostTp1MaxR - currentR);
            ctx.Tp1SmartExitHit = true;
            ctx.Tp1SmartExitType = "TP1_SMART";
            ctx.Tp1SmartExitReason = "TREND_COLLAPSE";
            ctx.Tp1SmartExitR = currentR;
            ctx.Tp1SmartBarsSinceTp1 = barsSinceTp1;
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

            double baseR = ctx.Tp2R > 0 ? ctx.Tp2R : 1.0;
            double desiredR = baseR * decision.Tp2ExtensionMultiplier;
            double currentR = ctx.Tp2ExtensionMultiplierApplied > 0
                ? baseR * ctx.Tp2ExtensionMultiplierApplied
                : baseR;

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

            double? currentPrice = null;
            if (TryResolveExitSymbol(pos, out var sym, ctx))
                currentPrice = IsLong(ctx) ? sym.Bid : sym.Ask;

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[EXIT] TP2 EXTENDED symbol={pos.SymbolName} positionId={pos.Id} " +
                $"direction={pos.TradeType} currentPrice={currentPrice} " +
                $"oldTp={currentTp} newTp={newTp}", ctx, pos));

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
            {
                GlobalLogger.Log(_bot, $"[EXIT][SKIP] reason=position_null symbol={_bot.SymbolName}");
                return;
            }

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
