using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;
using GeminiV26.Core.TradeManagement;

namespace GeminiV26.Instruments.BTCUSD
{
    /// <summary>
    /// BTCUSD ExitManager
    /// - Régi BTC TP1 logika megtartva (M1 wick touch)
    /// - TP1 előtt nincs trailing
    /// - TP1 után: BE + TTM + adaptive trailing + optional TP2 extension
    /// </summary>
    public class BtcUsdExitManager
    {
        private readonly Robot _bot;

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
            _contexts[Convert.ToInt64(ctx.PositionId)] = ctx;
        }

        // =========================================================
        // BAR-LEVEL EXIT MANAGEMENT
        // =========================================================
        public void OnBar(Position position)
        {
            if (position == null)
                return;

            long key = Convert.ToInt64(position.Id);

            if (!_contexts.TryGetValue(key, out var ctx) || ctx == null)
                return;

            ctx.BarsSinceEntryM5++;
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

                Position pos = null;
                foreach (var p in _bot.Positions)
                {
                    if (Convert.ToInt64(p.Id) == key)
                    {
                        pos = p;
                        break;
                    }
                }

                if (pos == null)
                    continue;

                if (!pos.StopLoss.HasValue)
                    continue;

                var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
                if (sym == null)
                    continue;

                double rDist = ctx.RiskPriceDistance;
                if (rDist <= 0)
                    continue;

                // =========================
                // TP1 (legacy crypto-touch: M1 wick)
                // =========================
                if (!ctx.Tp1Hit)
                {
                    if (ctx.Tp1R <= 0)
                        ctx.Tp1R = 0.5;

                    double tp1Price =
                        ctx.Tp1Price.HasValue && ctx.Tp1Price.Value > 0
                            ? ctx.Tp1Price.Value
                            : ctx.EntryPrice + Direction(pos) * rDist * ctx.Tp1R;

                    if (!ctx.Tp1Price.HasValue || ctx.Tp1Price.Value <= 0)
                        ctx.Tp1Price = tp1Price;

                    var m1 = _bot.MarketData.GetBars(TimeFrame.Minute1, pos.SymbolName);

                    bool reached;
                    if (m1 != null && m1.Count > 0)
                    {
                        var m1Bar = m1.LastBar;

                        reached = pos.TradeType == TradeType.Buy
                            ? m1Bar.High >= tp1Price
                            : m1Bar.Low <= tp1Price;

                        _bot.Print(
                            $"[BTCUSD][TP1][CHECK] pos={pos.Id} " +
                            $"tp1={tp1Price} m1High={m1Bar.High} m1Low={m1Bar.Low} " +
                            $"bid={sym.Bid} ask={sym.Ask} reached={reached}"
                        );
                    }
                    else
                    {
                        reached =
                            (pos.TradeType == TradeType.Buy && sym.Bid >= tp1Price) ||
                            (pos.TradeType == TradeType.Sell && sym.Ask <= tp1Price);

                        _bot.Print(
                            $"[BTCUSD][TP1][CHECK:FALLBACK] pos={pos.Id} " +
                            $"tp1={tp1Price} bid={sym.Bid} ask={sym.Ask} reached={reached}"
                        );
                    }

                    if (reached)
                    {
                        _bot.Print(
                            $"[BTCUSD][TP1][HIT] symbol={pos.SymbolName} pos={pos.Id} " +
                            $"dir={pos.TradeType} tp1={tp1Price} rDist={rDist} tp1R={ctx.Tp1R}"
                        );

                        ExecuteTp1(pos, ctx, rDist);

                        // Legacy viselkedés:
                        // TP1 után azonnal kilépünk ebből a tickből,
                        // hogy ne legyen state-ütközés partial close / modify miatt.
                        return;
                    }

                    // =========================
                    // TVM – Early Exit (TP1 előtt)
                    // =========================
                    {
                        const int MinBarsBeforeTvm = 4;

                        ctx.BarsSinceEntryM5 = (int)Math.Max(
                            1,
                            (_bot.Server.Time - ctx.EntryTime).TotalSeconds / 300.0
                        );

                        if (ctx.BarsSinceEntryM5 >= MinBarsBeforeTvm)
                        {
                            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5, pos.SymbolName);
                            var m15 = _bot.MarketData.GetBars(TimeFrame.Minute15, pos.SymbolName);

                            if (_tvm.ShouldEarlyExit(ctx, pos, m5, m15))
                            {
                                _bot.Print(
                                    $"[TVM EXIT] {pos.SymbolName} pos={pos.Id} " +
                                    $"reason={ctx.DeadTradeReason} " +
                                    $"MFE_R={ctx.MfeR:0.00} MAE_R={ctx.MaeR:0.00} " +
                                    $"barsM5={ctx.BarsSinceEntryM5}"
                                );

                                _bot.ClosePosition(pos);
                                _contexts.Remove(key);
                                return;
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

                _bot.Print(
                    $"[BTCUSD][TTM] pos={pos.Id} " +
                    $"score={decision.Score:0.00} state={decision.State} " +
                    $"mode={decision.TrailingMode} allowTp2Ext={decision.AllowTp2Extension} " +
                    $"mult={decision.Tp2ExtensionMultiplier:0.00}"
                );

                TryExtendTp2(pos, ctx, decision);

                _bot.Print(
                    $"[EXIT] TRAILING ACTIVE symbol={pos.SymbolName} positionId={pos.Id} " +
                    $"direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} " +
                    $"sl={pos.StopLoss} tp={pos.TakeProfit}"
                );

                _adaptiveTrailingEngine.Apply(pos, ctx, decision, structure, profile);
            }
        }

        // =========================================================
        // Manage() megmarad, de exit logika OnTick-ben fut
        // =========================================================
        public void Manage(Position pos)
        {
            _bot.Print("[BTCUSD][INFO] Manage() called, exit handled in OnTick()");
        }

        // =========================================================
        // TP1 EXECUTION (legacy BTC flow)
        // =========================================================
        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
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
                _bot.Print(
                    $"[BTCUSD][TP1][SKIP] partial close below min " +
                    $"pos={pos.Id} requested={closeUnits} min={minUnits}"
                );
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
                _bot.Print(
                    $"[BTCUSD][TP1][SKIP] adjusted partial close below min " +
                    $"pos={pos.Id} adjusted={closeUnits} min={minUnits}"
                );
                return;
            }

            var closeResult = _bot.ClosePosition(pos, closeUnits);
            if (!closeResult.IsSuccessful)
            {
                _bot.Print($"[BTCUSD][TP1][FAIL] partial close failed pos={pos.Id}");
                return;
            }

            _bot.Print(
                $"[EXIT] PARTIAL CLOSE executed symbol={pos.SymbolName} positionId={pos.Id} " +
                $"direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} " +
                $"closedUnits={closeUnits}"
            );

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = Math.Max(0, pos.VolumeInUnits - closeUnits);

            // 2) TP1 state
            ctx.Tp1Hit = true;

            // 3) BE / protective SL after TP1
            double bePrice = ctx.EntryPrice + Direction(pos) * rDist * BeOffsetR;

            // Soha ne rontsunk az SL-en
            if (pos.StopLoss.HasValue)
            {
                bePrice = pos.TradeType == TradeType.Buy
                    ? Math.Max(bePrice, pos.StopLoss.Value)
                    : Math.Min(bePrice, pos.StopLoss.Value);
            }

            _bot.ModifyPosition(pos, bePrice, pos.TakeProfit);

            _bot.Print(
                $"[EXIT] BE MOVE applied symbol={pos.SymbolName} positionId={pos.Id} " +
                $"direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} be={bePrice}"
            );

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;

            // 4) Trailing mode default
            if (ctx.TrailingMode == TrailingMode.None)
                ctx.TrailingMode = TrailingMode.Normal;
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
                pos.TradeType == TradeType.Buy
                    ? _bot.Symbol.Bid - trailDist
                    : _bot.Symbol.Ask + trailDist;

            if (ctx.BePrice > 0)
            {
                desiredSl = pos.TradeType == TradeType.Buy
                    ? Math.Max(desiredSl, ctx.BePrice)
                    : Math.Min(desiredSl, ctx.BePrice);
            }

            double minImprovePip = _bot.Symbol.PipSize * MinTrailImprovePips;
            double minImproveAtr = atr * 0.15;
            double minImprove = Math.Max(minImprovePip, minImproveAtr);

            bool improve =
                (pos.TradeType == TradeType.Buy && desiredSl > pos.StopLoss.Value + minImprove) ||
                (pos.TradeType == TradeType.Sell && desiredSl < pos.StopLoss.Value - minImprove);

            if (!improve)
                return;

            _bot.ModifyPosition(pos, desiredSl, pos.TakeProfit);
        }

        private static int Direction(Position pos)
            => pos.TradeType == TradeType.Buy ? 1 : -1;

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
            {
                if (!decision.AllowTp2Extension)
                    _bot.Print("[TTM] TP2 extension skipped=notAllowed");
                return;
            }

            double baseR = ctx.Tp2R > 0 ? ctx.Tp2R : 1.0;
            double desiredR = baseR * decision.Tp2ExtensionMultiplier;
            double currentR = ctx.Tp2ExtensionMultiplierApplied > 0
                ? baseR * ctx.Tp2ExtensionMultiplierApplied
                : baseR;

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

            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            _bot.Print(
                $"[EXIT] TP2 EXTENDED symbol={pos.SymbolName} positionId={pos.Id} " +
                $"direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym?.Bid : sym?.Ask)} " +
                $"oldTp={currentTp} newTp={newTp}"
            );

            ctx.LastExtendedTp2 = newTp;
            ctx.Tp2ExtensionMultiplierApplied = desiredR / baseR;

            _bot.Print($"[TTM] TP2 extended from {currentTp} to {newTp}");
        }
    }
}