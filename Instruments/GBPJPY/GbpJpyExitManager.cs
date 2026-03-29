using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Internals;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using GeminiV26.Core.TradeManagement;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.GBPJPY
{
    public class GbpJpyExitManager : IExitManager
    {
        private readonly Robot _bot;
        private readonly RuntimeSymbolResolver _runtimeSymbols;
        private readonly TradeViabilityMonitor _tvm;
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;

        private readonly Dictionary<long, PositionContext> _contexts = new();
        private readonly HashSet<long> _rehydratedResolverSkipLogged = new();

        private const double BeOffsetR = 0.10;

        public GbpJpyExitManager(Robot bot)
        {
            _bot = bot;
            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
            _tvm = new TradeViabilityMonitor(_bot);
            _trendTradeManager = new TrendTradeManager(_bot, _bot.Bars);
            _adaptiveTrailingEngine = new AdaptiveTrailingEngine(_bot);
            _structureTracker = new StructureTracker(_bot, _bot.Bars);
        }

        public void RegisterContext(PositionContext ctx)
        {
            if (ctx == null)
                return;

            if (ctx.FinalDirection == TradeDirection.None)
            {
                if (!ctx.MissingDirLogged)
                {
                    GlobalLogger.Log($"[DIR][ERROR] Missing FinalDirection posId={ctx.PositionId}");
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

            GlobalLogger.Log(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx));
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx));
        }

        private bool TryResolveExitSymbol(Position pos, out Symbol symbol, PositionContext ctx = null)
        {
            symbol = null;

            if (pos == null)
            {
                GlobalLogger.Log("[RESOLVER][EXIT_SKIP] symbol=UNKNOWN positionId=0 reason=position_null");
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
                        GlobalLogger.Log($"[RESOLVER][EXIT_RECOVER] symbol={pos.SymbolName} positionId={pos.Id} source=platform_symbols");
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

                GlobalLogger.Log($"[RESOLVER][EXIT_RECOVER] symbol={pos.SymbolName} positionId={pos.Id} source=platform_symbols");
                return true;
            }

            if (isRehydratedContext)
                _rehydratedResolverSkipLogged.Add(Convert.ToInt64(ctx.PositionId));

            GlobalLogger.Log($"[RESOLVER][EXIT_SKIP] symbol={pos.SymbolName} positionId={pos.Id} reason=unresolved_runtime_symbol");
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
                    GlobalLogger.Log($"[RESOLVER][EXIT_SKIP] symbol={pos?.SymbolName ?? "UNKNOWN"} positionId={pos?.Id ?? 0} reason=unresolved_runtime_symbol");

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
                return;

            long key = Convert.ToInt64(position.Id);

            if (!_contexts.TryGetValue(key, out var ctx) || ctx == null)
                return;

            ctx.BarsSinceEntryM5++;

            if (TryResolveExitSymbol(position, out var stateSymbol, ctx))
            {
                string stateFingerprint = $"{ctx.BarsSinceEntryM5}|{ctx.Tp1Hit}|{ctx.BeActivated}|{ctx.TrailingActivated}|{ctx.TrailSteps}";
                if (ctx.LastStateTraceBarIndex != ctx.BarsSinceEntryM5 || !string.Equals(ctx.LastStateTraceFingerprint, stateFingerprint, StringComparison.Ordinal))
                {
                    GlobalLogger.Log(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildStateSnapshot(ctx, position, stateSymbol), ctx, position));
                    ctx.LastStateTraceBarIndex = ctx.BarsSinceEntryM5;
                    ctx.LastStateTraceFingerprint = stateFingerprint;
                }
            }
        }

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

                    GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[EXIT][CLEANUP]\nreason=position_not_found", ctx));
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
                GlobalLogger.Log($"[ONTICK] time={_bot.Server.Time:HH:mm:ss.fff}");
                GlobalLogger.Log($"[MFE_CALLSITE] symbol={_bot.SymbolName} price={currentPrice}");
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
                        GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[EXIT][TP1] symbol={pos.SymbolName} positionId={pos.Id} price={tp1Price:0.#####}", ctx, pos));
                        GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[TP1][TOUCHED]\npos={pos.Id}\ntp1={tp1Price:0.#####}", ctx, pos));
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
                            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[EXIT][DECISION]\nreason={ctx.DeadTradeReason}\ndetail=tvm_early_exit", ctx, pos));
                            GlobalLogger.Log(TradeLogIdentity.WithPositionIds("[EXIT SNAPSHOT]\n" +
                                $"symbol={pos.SymbolName}\n" +
                                $"positionId={pos.Id}\n" +
                                $"mfe={ctx.MfeR:0.##}\n" +
                                $"mae={ctx.MaeR:0.##}\n" +
                                $"tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}\n" +
                                $"barsOpen={ctx.BarsSinceEntryM5}\n" +
                                $"reason={ctx.DeadTradeReason}", ctx, pos));
                            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[EXIT] reason={ctx.DeadTradeReason}", ctx, pos));
                            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[GBPJPY][TVM][EXIT] pos={pos.Id} reason={ctx.DeadTradeReason}", ctx, pos));
                            ctx.IsFullyClosing = true;
                            _bot.ClosePosition(pos);
                            _contexts.Remove(key);
                    _rehydratedResolverSkipLogged.Remove(key);
                            continue;
                        }
                    }
                    continue;
                }

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

        public void Manage(Position pos)
        {
        }

        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            if (!TryResolveExitSymbol(pos, out var sym, ctx))
                return;

            double frac = ctx.Tp1CloseFraction;
            if (frac <= 0 || frac >= 1)
                frac = 0.5;

            long closeUnits = (long)sym.NormalizeVolumeInUnits(
                (long)Math.Floor(pos.VolumeInUnits * frac),
                RoundingMode.Down
            );

            long minUnits = (long)sym.VolumeInUnitsMin;
            if (closeUnits < minUnits)
                return;

            if (closeUnits >= pos.VolumeInUnits)
                closeUnits = (long)sym.NormalizeVolumeInUnits(
                    (long)Math.Floor(pos.VolumeInUnits - minUnits),
                    RoundingMode.Down
                );

            if (closeUnits < minUnits)
                return;

            var closeResult = _bot.ClosePosition(pos, closeUnits);
            if (!closeResult.IsSuccessful)
            {
                GlobalLogger.Log("[TP1][FAIL] execution failed");
                return;
            }

            double executionPrice = IsLong(ctx) ? sym.Bid : sym.Ask;
            GlobalLogger.Log($"[TP1][EXECUTED] volumeClosed={closeUnits} price={executionPrice}");

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = Math.Max(0, pos.VolumeInUnits - closeUnits);
            ctx.Tp1Hit = true;
            GlobalLogger.Log($"[TP1] hit → MFE continues tracking");
            ctx.Tp1Executed = true;
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[TP1][EXECUTED]\ntp1={ctx.Tp1Price:0.#####}\nclosedUnits={closeUnits}", ctx, pos));

            var live = _bot.Positions.Find(pos.Label, pos.SymbolName, pos.TradeType);
            if (live == null)
            {
                GlobalLogger.Log("[EXIT][POST_TP1_NO_POSITION]");
                _contexts.Remove(Convert.ToInt64(pos.Id));
                return;
            }

            if (live.VolumeInUnits < sym.VolumeInUnitsMin)
            {
                GlobalLogger.Log("[EXIT][POST_TP1_MIN_VOLUME]");
                _contexts.Remove(Convert.ToInt64(pos.Id));
                return;
            }

            ApplyBreakEven(live, ctx, rDist);
        }

        private void ApplyBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            double bePrice =
                IsLong(ctx)
                    ? pos.EntryPrice + rDist * BeOffsetR
                    : pos.EntryPrice - rDist * BeOffsetR;

            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[BE][REQUEST]\nbePrice={bePrice:0.#####}\ntp={pos.TakeProfit}", ctx, pos));
            SafeModify(pos, bePrice, pos.TakeProfit);

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;
            ctx.BeActivated = true;
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[BE][SUCCESS]\nbePrice={bePrice:0.#####}\ntp={pos.TakeProfit}", ctx, pos));
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[BE] moved", ctx, pos));

            if (ctx.TrailingMode == TrailingMode.None)
                ctx.TrailingMode = TrailingMode.Normal;
        }

        private double ResolveTp1R(PositionContext ctx)
        {
            if (ctx.Tp1R > 0)
                return ctx.Tp1R;

            if (ctx.FinalConfidence >= 85)
                return 0.40;

            if (ctx.FinalConfidence >= 70)
                return 0.50;

            return 0.60;
        }

        private double GetRiskDistance(Position pos, PositionContext ctx)
        {
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

        private void TryExtendTp2(Position pos, PositionContext ctx, TrendDecision decision)
        {
            if (!decision.AllowTp2Extension || !ctx.Tp2Price.HasValue)
                return;

            double baseR = ctx.Tp2R > 0 ? ctx.Tp2R : 1.0;
            double desiredR = baseR * decision.Tp2ExtensionMultiplier;

            double newTp = IsLong(ctx)
                ? pos.EntryPrice + ctx.RiskPriceDistance * desiredR
                : pos.EntryPrice - ctx.RiskPriceDistance * desiredR;

            double currentTp = pos.TakeProfit ?? ctx.Tp2Price.Value;
            bool outward = IsLong(ctx) ? newTp > currentTp : newTp < currentTp;

            if (!outward)
                return;

            SafeModify(pos, pos.StopLoss, newTp);
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

            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[MODIFY][REQUEST]\nsl={sl}\ntp={tp}", position.Id, null, position.SymbolName));
            var livePos = FindPosition(Convert.ToInt64(position.Id));
            if (livePos == null)
            {
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[MODIFY][FAIL]\nsl={sl}\ntp={tp}\nerror=POSITION_NOT_FOUND", position.Id, null, position.SymbolName));
                GlobalLogger.Log($"[SAFE_MODIFY][SKIP_NO_POSITION] pos={position.Id}");
                return;
            }

            var result = _bot.ModifyPosition(livePos, sl, tp);
            if (!result.IsSuccessful)
            {
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[MODIFY][FAIL]\nsl={sl}\ntp={tp}\nerror={result.Error}", position.Id, null, position.SymbolName));
                GlobalLogger.Log($"[SAFE_MODIFY][FAIL] {position.Id} error={result.Error}");
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[BE][FAIL]\nsl={sl}\ntp={tp}\nerror={result.Error}", position.Id, null, position.SymbolName));
                return;
            }

            GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[MODIFY][SUCCESS]\nsl={sl}\ntp={tp}", position.Id, null, position.SymbolName));
        }

        private static bool IsLong(PositionContext ctx)
        {
            return ctx?.FinalDirection == TradeDirection.Long;
        }

    }
}
