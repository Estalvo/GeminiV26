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

        private readonly TradeViabilityMonitor _tvm;
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;

        // ===== TP1 / BE =====
        private const double BeOffsetR = 0.05;

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
                    _bot.Print($"[DIR][ERROR] Missing FinalDirection posId={ctx.PositionId}");
                    ctx.MissingDirLogged = true;
                }

                return;
            }

            _contexts[Convert.ToInt64(ctx.PositionId)] = ctx;
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildContextCreate(ctx), ctx));
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildDirectionSnapshot(ctx), ctx));
        }

        // =========================================================
        // BAR-LEVEL EXIT MANAGEMENT
        // =========================================================
        private bool TryResolveExitSymbol(Position pos, out Symbol symbol)
        {
            symbol = null;

            if (pos == null)
            {
                _bot.Print("[RESOLVER][EXIT_SKIP] symbol=UNKNOWN positionId=0 reason=position_null");
                return false;
            }

            if (_runtimeSymbols.TryGetSymbolMeta(pos.SymbolName, out symbol) && symbol != null)
                return true;

            symbol = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (symbol != null)
            {
                _bot.Print($"[RESOLVER][EXIT_RECOVER] symbol={pos.SymbolName} positionId={pos.Id} source=platform_symbols");
                return true;
            }

            _bot.Print($"[RESOLVER][EXIT_SKIP] symbol={pos.SymbolName} positionId={pos.Id} reason=unresolved_runtime_symbol");
            return false;
        }

        private bool TryGetExitBars(Position pos, TimeFrame timeFrame, out Bars bars)
        {
            bars = null;
            if (pos == null || !_runtimeSymbols.TryGetBars(timeFrame, pos.SymbolName, out bars))
            {
                _bot.Print($"[RESOLVER][EXIT_SKIP] symbol={pos?.SymbolName ?? "UNKNOWN"} positionId={pos?.Id ?? 0} reason=unresolved_runtime_symbol");
                return false;
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

            if (TryResolveExitSymbol(position, out var stateSymbol))
            {
                string stateFingerprint = $"{ctx.BarsSinceEntryM5}|{ctx.Tp1Hit}|{ctx.BeActivated}|{ctx.TrailingActivated}|{ctx.TrailSteps}";
                if (ctx.LastStateTraceBarIndex != ctx.BarsSinceEntryM5 || !string.Equals(ctx.LastStateTraceFingerprint, stateFingerprint, StringComparison.Ordinal))
                {
                    _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildStateSnapshot(ctx, position, stateSymbol), ctx, position));
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

                    _bot.Print(TradeLogIdentity.WithPositionIds($"[EXIT][CLEANUP]\nreason=position_not_found", ctx));
                    _contexts.Remove(key);
                    continue;
                }

                if (!pos.StopLoss.HasValue)
                    continue;

                if (!TryResolveExitSymbol(pos, out var sym))
                    continue;

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

                    bool reached =
                        IsLong(ctx)
                            ? sym.Bid >= tp1Price
                            : sym.Ask <= tp1Price;

                    if (!reached)
                    {
                        if (TryGetExitBars(pos, TimeFrame.Minute, out var m1) && m1.Count > 0)
                        {
                            var m1Bar = m1.LastBar;

                            reached = IsLong(ctx)
                                ? m1Bar.High >= tp1Price
                                : m1Bar.Low <= tp1Price;
                        }
                    }

                    if (reached)
                    {
                        _bot.Print(TradeLogIdentity.WithPositionIds($"[EXIT][TP1] symbol={pos.SymbolName} positionId={pos.Id} price={tp1Price:0.#####}", ctx, pos));
                        _bot.Print(TradeLogIdentity.WithPositionIds($"[TP1][HIT]\npos={pos.Id}\ntp1={tp1Price:0.#####}", ctx, pos));
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
                            if (!TryGetExitBars(pos, TimeFrame.Minute5, out var m5) ||
                            !TryGetExitBars(pos, TimeFrame.Minute15, out var m15))
                        {
                            continue;
                        }

                        if (_tvm.ShouldEarlyExit(ctx, pos, m5, m15))
                            {
                                _bot.Print(TradeLogIdentity.WithPositionIds($"[EXIT][DECISION]\nreason={ctx.DeadTradeReason}\ndetail=tvm_early_exit", ctx, pos));
                            _bot.Print(TradeLogIdentity.WithPositionIds("[EXIT SNAPSHOT]\n" +
                                    $"symbol={pos.SymbolName}\n" +
                                    $"positionId={pos.Id}\n" +
                                    $"mfe={ctx.MfeR:0.##}\n" +
                                    $"mae={ctx.MaeR:0.##}\n" +
                                    $"tp1Hit={ctx.Tp1Hit.ToString().ToLowerInvariant()}\n" +
                                    $"barsOpen={ctx.BarsSinceEntryM5}\n" +
                                    $"reason={ctx.DeadTradeReason}", ctx, pos));
                                _bot.Print(TradeLogIdentity.WithPositionIds($"[EXIT] reason={ctx.DeadTradeReason}", ctx, pos));
                                _bot.Print(TradeLogIdentity.WithPositionIds(
                                    $"[TVM EXIT] {pos.SymbolName} pos={pos.Id} " +
                                    $"reason={ctx.DeadTradeReason} " +
                                    $"MFE_R={ctx.MfeR:0.00} MAE_R={ctx.MaeR:0.00} " +
                                    $"barsM5={ctx.BarsSinceEntryM5}", ctx, pos));

                                ctx.IsFullyClosing = true;
                                _bot.ClosePosition(pos);
                                _contexts.Remove(key);
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
            if (!TryResolveExitSymbol(pos, out var sym))
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
                _bot.Print(TradeLogIdentity.WithPositionIds(
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
                _bot.Print(TradeLogIdentity.WithPositionIds(
                    $"[BTCUSD][TP1][SKIP] adjusted partial close below min " +
                    $"pos={pos.Id} adjusted={closeUnits} min={minUnits}", ctx, pos));
                return;
            }

            var closeResult = _bot.ClosePosition(pos, closeUnits);
            if (!closeResult.IsSuccessful)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds($"[BTCUSD][TP1][FAIL] partial close failed pos={pos.Id}", ctx, pos));
                return;
            }

            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[EXIT] PARTIAL CLOSE executed symbol={pos.SymbolName} positionId={pos.Id} " +
                $"direction={pos.TradeType} currentPrice={(IsLong(ctx) ? sym.Bid : sym.Ask)} " +
                $"closedUnits={closeUnits}", ctx, pos));

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = Math.Max(0, pos.VolumeInUnits - closeUnits);

            // 2) TP1 state
            ctx.Tp1Hit = true;
            ctx.Tp1Executed = true;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[TP1][EXECUTED]\ntp1={ctx.Tp1Price:0.#####}\nclosedUnits={closeUnits}", ctx, pos));

            // 3) BE / protective SL after TP1
            var live = _bot.Positions.Find(pos.Label, pos.SymbolName, pos.TradeType);
            if (live == null)
            {
                _bot.Print("[EXIT][POST_TP1_NO_POSITION]");
                _contexts.Remove(Convert.ToInt64(pos.Id));
                return;
            }

            if (live.VolumeInUnits < sym.VolumeInUnitsMin)
            {
                _bot.Print("[EXIT][POST_TP1_MIN_VOLUME]");
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

            _bot.Print(TradeLogIdentity.WithPositionIds($"[BE][REQUEST]\nbePrice={bePrice:0.#####}\ntp={pos.TakeProfit}", ctx, pos));
            SafeModify(pos, bePrice, pos.TakeProfit);

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;
            ctx.BeActivated = true;
            _bot.Print(TradeLogIdentity.WithPositionIds($"[BE][SUCCESS]\nbePrice={bePrice:0.#####}\ntp={pos.TakeProfit}", ctx, pos));
            _bot.Print(TradeLogIdentity.WithPositionIds($"[BE] moved", ctx, pos));

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

        private void TryExtendTp2(Position pos, PositionContext ctx, TrendDecision decision)
        {
            if (!decision.AllowTp2Extension || !ctx.Tp2Price.HasValue || !ctx.Tp2Price.Value.Equals(pos.TakeProfit ?? ctx.Tp2Price.Value))
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
                return;

            if (ctx.LastExtendedTp2.HasValue && Math.Abs(ctx.LastExtendedTp2.Value - newTp) < _bot.Symbol.PipSize)
                return;

            SafeModify(pos, pos.StopLoss, newTp);

            double? currentPrice = null;
            if (TryResolveExitSymbol(pos, out var sym))
                currentPrice = IsLong(ctx) ? sym.Bid : sym.Ask;

            _bot.Print(TradeLogIdentity.WithPositionIds(
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
                return;

            _bot.Print(TradeLogIdentity.WithPositionIds($"[MODIFY][REQUEST]\nsl={sl}\ntp={tp}", position.Id, null, position.SymbolName));
            var livePos = FindPosition(Convert.ToInt64(position.Id));
            if (livePos == null)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds($"[MODIFY][FAIL]\nsl={sl}\ntp={tp}\nerror=POSITION_NOT_FOUND", position.Id, null, position.SymbolName));
                _bot.Print($"[SAFE_MODIFY][SKIP_NO_POSITION] pos={position.Id}");
                return;
            }

            var result = _bot.ModifyPosition(livePos, sl, tp);
            if (!result.IsSuccessful)
            {
                _bot.Print(TradeLogIdentity.WithPositionIds($"[MODIFY][FAIL]\nsl={sl}\ntp={tp}\nerror={result.Error}", position.Id, null, position.SymbolName));
                _bot.Print($"[SAFE_MODIFY][FAIL] {position.Id} error={result.Error}");
                _bot.Print(TradeLogIdentity.WithPositionIds($"[BE][FAIL]\nsl={sl}\ntp={tp}\nerror={result.Error}", position.Id, null, position.SymbolName));
                return;
            }

            _bot.Print(TradeLogIdentity.WithPositionIds($"[MODIFY][SUCCESS]\nsl={sl}\ntp={tp}", position.Id, null, position.SymbolName));
        }

        private static bool IsLong(PositionContext ctx)
        {
            return ctx?.FinalDirection == TradeDirection.Long;
        }

    }
}
