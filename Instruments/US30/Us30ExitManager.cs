using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.TradeManagement;

namespace GeminiV26.Instruments.US30
{
    public class Us30ExitManager
    {
        private readonly Robot _bot;
        private readonly TradeViabilityMonitor _tvm;
        private readonly Dictionary<long, PositionContext> _contexts = new();
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;

        private const double BeOffsetR = 0.05;

        public Us30ExitManager(Robot bot)
        {
            _bot = bot;
            _tvm = new TradeViabilityMonitor(_bot);
            _trendTradeManager = new TrendTradeManager(_bot, _bot.Bars);
            _adaptiveTrailingEngine = new AdaptiveTrailingEngine(_bot);
            _structureTracker = new StructureTracker(_bot, _bot.Bars);
        }

        public void RegisterContext(PositionContext ctx)
        {
            _contexts[Convert.ToInt64(ctx.PositionId)] = ctx;
        }

        public void OnTick()
        {
            var keys = new List<long>(_contexts.Keys);

            foreach (var key in keys)
            {
                if (!_contexts.TryGetValue(key, out var ctx))
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

                if (pos == null || !pos.StopLoss.HasValue)
                    continue;

                var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
                if (sym == null)
                    continue;

                double rDist = ctx.RiskPriceDistance > 0
                    ? ctx.RiskPriceDistance
                    : Math.Abs(pos.EntryPrice - pos.StopLoss.Value);

                if (rDist <= 0)
                    continue;

                if (!ctx.Tp1Hit)
                {
                    double tp1R = ctx.Tp1R > 0 ? ctx.Tp1R : 0.5;
                    if (ctx.Tp1R <= 0)
                        ctx.Tp1R = tp1R;

                    if (CheckTp1Hit(pos, ctx, rDist, tp1R))
                    {
                        bool tp1Done = ExecuteTp1(pos, ctx);

                        if (tp1Done)
                        {
                            MoveToBreakEven(pos, ctx, rDist);
                            _bot.Print($"[US30 TP1 STATE] pos={pos.Id} tp1Hit={ctx.Tp1Hit} be={ctx.BePrice}");
                        }

                        return;
                    }
                }

                if (!ctx.Tp1Hit)
                {
                    const int MinBarsBeforeTvm = 4;
                    ctx.BarsSinceEntryM5 = (int)Math.Max(1, (_bot.Server.Time - ctx.EntryTime).TotalSeconds / 300.0);

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

                    continue;
                }

                // post-TP1 delegated management
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

        public void OnBar(Position pos)
        {
            // reserved
        }

        private bool CheckTp1Hit(Position pos, PositionContext ctx, double rDist, double tp1R)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return false;

            double tp1Price = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + rDist * tp1R
                : pos.EntryPrice - rDist * tp1R;

            double priceNow = pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask;

            bool hit = pos.TradeType == TradeType.Buy ? priceNow >= tp1Price : priceNow <= tp1Price;

            _bot.Print(
                $"[US30 TP1 DBG] pos={pos.Id} dir={pos.TradeType} entry={pos.EntryPrice} sl={pos.StopLoss} " +
                $"r={rDist} tp1R={tp1R} tp1={tp1Price} bid={sym.Bid} ask={sym.Ask} tp1Hit={ctx.Tp1Hit} hit={hit}"
            );

            return hit;
        }

        private bool ExecuteTp1(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return false;

            double frac = ctx.Tp1CloseFraction > 0 && ctx.Tp1CloseFraction < 1 ? ctx.Tp1CloseFraction : 0.5;
            long minUnits = (long)sym.VolumeInUnitsMin;
            long targetUnits = (long)Math.Floor(pos.VolumeInUnits * frac);
            if (targetUnits <= 0)
                return false;

            long closeUnits = (long)sym.NormalizeVolumeInUnits(targetUnits);
            if (closeUnits < minUnits)
                return false;

            if (closeUnits >= pos.VolumeInUnits)
                closeUnits = (long)(pos.VolumeInUnits - minUnits);

            if (closeUnits <= 0)
                return false;

            var closeResult = _bot.ClosePosition(pos, closeUnits);
            _bot.Print($"[US30 TP1 EXEC RES] pos={pos.Id} success={closeResult.IsSuccessful} err={closeResult.Error}");

            if (!closeResult.IsSuccessful)
                return false;

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = pos.VolumeInUnits - closeUnits;
            ctx.Tp1Hit = true;
            return true;
        }

        private void MoveToBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            if (ctx.BePrice > 0)
                return;

            double bePrice = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + rDist * BeOffsetR
                : pos.EntryPrice - rDist * BeOffsetR;

            bool improve =
                (pos.TradeType == TradeType.Buy && bePrice > pos.StopLoss.Value) ||
                (pos.TradeType == TradeType.Sell && bePrice < pos.StopLoss.Value);

            if (!improve)
                return;

            _bot.ModifyPosition(pos, bePrice, pos.TakeProfit);
            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;
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
            ctx.LastExtendedTp2 = newTp;
            ctx.Tp2ExtensionMultiplierApplied = desiredR / baseR;
            _bot.Print($"[TTM] TP2 extended from {currentTp} to {newTp}");
        }
    }
}
