using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.TradeManagement;

namespace GeminiV26.Instruments.GER40
{
    public class Ger40ExitManager
    {
        private readonly Robot _bot;
        private readonly TradeViabilityMonitor _tvm;
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;

        private readonly Dictionary<long, PositionContext> _contexts = new();

        private const double BeOffsetR = 0.10;

        public Ger40ExitManager(Robot bot)
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

        public void OnBar(Position position)
        {
            if (position == null)
                return;

            long key = Convert.ToInt64(position.Id);

            if (!_contexts.TryGetValue(key, out var ctx) || ctx == null)
                return;

            ctx.BarsSinceEntryM5++;
        }

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

                if (pos == null || !pos.StopLoss.HasValue)
                    continue;

                var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
                if (sym == null)
                    continue;

                double rDist = ctx.RiskPriceDistance;
                if (rDist <= 0)
                    continue;

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

                    var m1 = _bot.MarketData.GetBars(TimeFrame.Minute, pos.SymbolName);

                    bool reached;
                    if (m1 != null && m1.Count > 0)
                    {
                        var m1Bar = m1.LastBar;
                        reached = pos.TradeType == TradeType.Buy
                            ? m1Bar.High >= tp1Price
                            : m1Bar.Low <= tp1Price;
                    }
                    else
                    {
                        reached =
                            (pos.TradeType == TradeType.Buy && sym.Bid >= tp1Price) ||
                            (pos.TradeType == TradeType.Sell && sym.Ask <= tp1Price);
                    }

                    if (reached)
                    {
                        _bot.Print($"[GER40][TP1][HIT] pos={pos.Id} tp1={tp1Price}");
                        ExecuteTp1(pos, ctx, rDist);
                        return;
                    }

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
                            _bot.Print($"[GER40][TVM][EXIT] pos={pos.Id} reason={ctx.DeadTradeReason}");
                            _bot.ClosePosition(pos);
                            _contexts.Remove(key);
                            return;
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
            _bot.Print($"[GER40][INFO] Manage() called, exit handled in OnTick()");
        }

        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
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
                return;

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = Math.Max(0, pos.VolumeInUnits - closeUnits);
            ctx.Tp1Hit = true;

            double bePrice = ctx.EntryPrice + Direction(pos) * rDist * BeOffsetR;
            if (pos.StopLoss.HasValue)
            {
                bePrice = pos.TradeType == TradeType.Buy
                    ? Math.Max(bePrice, pos.StopLoss.Value)
                    : Math.Min(bePrice, pos.StopLoss.Value);
            }

            _bot.ModifyPosition(pos, bePrice, pos.TakeProfit);

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;

            if (ctx.TrailingMode == TrailingMode.None)
                ctx.TrailingMode = TrailingMode.Normal;
        }

        private static int Direction(Position pos)
            => pos.TradeType == TradeType.Buy ? 1 : -1;

        private void TryExtendTp2(Position pos, PositionContext ctx, TrendDecision decision)
        {
            if (!decision.AllowTp2Extension || !ctx.Tp2Price.HasValue)
                return;

            double baseR = ctx.Tp2R > 0 ? ctx.Tp2R : 1.0;
            double desiredR = baseR * decision.Tp2ExtensionMultiplier;

            double newTp = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + ctx.RiskPriceDistance * desiredR
                : pos.EntryPrice - ctx.RiskPriceDistance * desiredR;

            double currentTp = pos.TakeProfit ?? ctx.Tp2Price.Value;
            bool outward = pos.TradeType == TradeType.Buy ? newTp > currentTp : newTp < currentTp;

            if (!outward)
                return;

            _bot.ModifyPosition(pos, pos.StopLoss, newTp);
            ctx.LastExtendedTp2 = newTp;
            ctx.Tp2ExtensionMultiplierApplied = desiredR / baseR;
        }
    }
}
