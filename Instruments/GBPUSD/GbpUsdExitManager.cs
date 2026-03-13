using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;
using GeminiV26.Core.TradeManagement;
using GeminiV26.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeminiV26.Instruments.GBPUSD
{
    public class GbpUsdExitManager
    {
        private readonly Robot _bot;

        private readonly Dictionary<long, PositionContext> _contexts = new();
        private readonly TradeViabilityMonitor _tvm;
        private readonly TrendTradeManager _trendTradeManager;
        private readonly AdaptiveTrailingEngine _adaptiveTrailingEngine;
        private readonly StructureTracker _structureTracker;

        private const double BeOffsetR = 0.10;

        private const double AtrMultTight = 0.8;
        private const double AtrMultNormal = 1.4;
        private const double AtrMultLoose = 2.0;

        public GbpUsdExitManager(Robot bot)
        {
            _bot = bot;
            _tvm = new TradeViabilityMonitor(bot);
            _trendTradeManager = new TrendTradeManager(_bot, _bot.Bars);
            _adaptiveTrailingEngine = new AdaptiveTrailingEngine(_bot);
            _structureTracker = new StructureTracker(_bot, _bot.Bars);
        }

        public void RegisterContext(PositionContext ctx)
        {
            _contexts[ctx.PositionId] = ctx;
        }

        // =====================================================
        // BAR-LEVEL EXIT (Trade Viability Monitor)
        // =====================================================
        public void OnBar(Position pos)
        {
            if (!_contexts.TryGetValue(pos.Id, out var ctx))
                return;

            if (ctx.Tp1Hit)
                return;

            if (!pos.StopLoss.HasValue)
                return;

            var m5 = _bot.MarketData.GetBars(TimeFrame.Minute5, pos.SymbolName);
            var m15 = _bot.MarketData.GetBars(TimeFrame.Minute15, pos.SymbolName);

            if (_tvm.ShouldEarlyExit(ctx, pos, m5, m15))
            {
                _bot.Print(
                    $"[GBPUSD TVM EXIT] pos={pos.Id} " +
                    $"reason={ctx.DeadTradeReason} " +
                    $"MFE_R={ctx.MfeR:0.00} MAE_R={ctx.MaeR:0.00}"
                );

                _bot.ClosePosition(pos);
                ctx.ExitReason = ExitReason.EarlyExit;
            }
        }

        // =====================================================
        // TICK EXIT
        // =====================================================
        public void OnTick()
        {
            foreach (var ctx in _contexts.Values)
            {
                var pos = _bot.Positions.FirstOrDefault(p => p.Id == ctx.PositionId);
                if (pos == null || !pos.StopLoss.HasValue)
                    continue;

                var sym = _bot.Symbols.GetSymbol(pos.SymbolName);

                double rDist = ctx.RiskPriceDistance;
                if (rDist <= 0)
                    continue;

                if (!ctx.Tp1Hit)
                {
                    double tp1Trigger =
                        pos.TradeType == TradeType.Buy
                            ? pos.EntryPrice + rDist * ctx.Tp1R
                            : pos.EntryPrice - rDist * ctx.Tp1R;

                    if (CheckTp1Hit(pos, rDist, ctx.Tp1R))
                    {
                        if (!ExecuteTp1(pos, ctx))
                        {
                            _bot.Print($"[EXIT] PARTIAL CLOSE failed symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? _bot.Symbols.GetSymbol(pos.SymbolName)?.Bid : _bot.Symbols.GetSymbol(pos.SymbolName)?.Ask)} tp1={ctx.Tp1Price}");
                            continue;
                        }

                        ctx.Tp1Hit = true;
                        _bot.Print($"[EXIT] TP1 HIT symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? _bot.Symbols.GetSymbol(pos.SymbolName)?.Bid : _bot.Symbols.GetSymbol(pos.SymbolName)?.Ask)} tp1={ctx.Tp1Price}");
                        ApplyBreakEven(pos, ctx);
                        ctx.TrailingMode = ctx.FinalConfidence >= 80
                            ? TrailingMode.Loose
                            : TrailingMode.Tight;

                        continue;
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
                _bot.Print($"[EXIT] TRAILING ACTIVE symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? _bot.Symbols.GetSymbol(pos.SymbolName)?.Bid : _bot.Symbols.GetSymbol(pos.SymbolName)?.Ask)} sl={pos.StopLoss} tp={pos.TakeProfit}");
                _adaptiveTrailingEngine.Apply(pos, ctx, decision, structure, profile);
            }
        }

        // =====================================================
        // TP1 CHECK
        // =====================================================
        private bool CheckTp1Hit(Position pos, double rDist, double tp1R)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null || tp1R <= 0)
                return false;

            _contexts.TryGetValue(pos.Id, out var ctx);
            double tp1Price = ctx != null && ctx.Tp1Price > 0
                ? ctx.Tp1Price
                : (pos.TradeType == TradeType.Buy
                    ? pos.EntryPrice + rDist * tp1R
                    : pos.EntryPrice - rDist * tp1R);

            if (ctx != null && ctx.Tp1Price <= 0)
                ctx.Tp1Price = tp1Price;

            return pos.TradeType == TradeType.Buy
                ? sym.Bid >= tp1Price
                : sym.Ask <= tp1Price;
        }

        // =====================================================
        // TP1 EXECUTION
        // =====================================================
        private bool ExecuteTp1(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return false;

            double fraction = ctx.Tp1CloseFraction > 0 && ctx.Tp1CloseFraction < 1
                ? ctx.Tp1CloseFraction
                : 0.5;

            double rawUnitsD = pos.VolumeInUnits * fraction;
            long flooredUnits = (long)Math.Floor(rawUnitsD);
            long normalizedUnits = (long)sym.NormalizeVolumeInUnits(flooredUnits, RoundingMode.Down);
            long minUnits = (long)sym.VolumeInUnitsMin;

            if (normalizedUnits < minUnits)
                return false;

            if (normalizedUnits >= pos.VolumeInUnits)
                normalizedUnits = (long)sym.NormalizeVolumeInUnits((long)Math.Floor(pos.VolumeInUnits - minUnits), RoundingMode.Down);

            if (normalizedUnits < minUnits)
                return false;

            var result = _bot.ClosePosition(pos, normalizedUnits);
            if (!result.IsSuccessful)
                return false;

            ctx.Tp1ClosedVolumeInUnits = normalizedUnits;
            ctx.RemainingVolumeInUnits = Math.Max(0, pos.VolumeInUnits - normalizedUnits);
            _bot.Print($"[EXIT] PARTIAL CLOSE executed symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym.Bid : sym.Ask)} closedUnits={normalizedUnits} remainingUnits={ctx.RemainingVolumeInUnits}");
            return true;
        }

        // =====================================================
        // BREAK EVEN
        // =====================================================
        private void ApplyBreakEven(Position pos, PositionContext ctx)
        {
            if (ctx.BePrice > 0)
                return;

            double bePrice = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + ctx.RiskPriceDistance * BeOffsetR
                : pos.EntryPrice - ctx.RiskPriceDistance * BeOffsetR;

            _bot.ModifyPosition(pos, Normalize(bePrice, pos.SymbolName), pos.TakeProfit);

            ctx.BePrice = bePrice;
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            _bot.Print($"[EXIT] BE MOVE applied symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? sym?.Bid : sym?.Ask)} be={bePrice}");
        }

        // =====================================================
        // TRAILING
        // =====================================================
        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            var atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);

            double atrVal = atr.Result.LastValue;
            if (atrVal <= 0)
                return;

            double mult = ctx.TrailingMode switch
            {
                TrailingMode.Tight => AtrMultTight,
                TrailingMode.Normal => AtrMultNormal,
                TrailingMode.Loose => AtrMultLoose,
                _ => AtrMultNormal
            };

            double desiredSl = pos.TradeType == TradeType.Buy
                ? sym.Bid - atrVal * mult
                : sym.Ask + atrVal * mult;

            desiredSl = Normalize(desiredSl, pos.SymbolName);

            if (pos.TradeType == TradeType.Buy && desiredSl <= pos.StopLoss.Value)
                return;
            if (pos.TradeType == TradeType.Sell && desiredSl >= pos.StopLoss.Value)
                return;

            _bot.ModifyPosition(pos, desiredSl, pos.TakeProfit);
        }

        private double Normalize(double price, string symbol)
        {
            var s = _bot.Symbols.GetSymbol(symbol);
            return Math.Round(Math.Round(price / s.TickSize) * s.TickSize, s.Digits);
        }

        private double GetCurrentR(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);

            double move = pos.TradeType == TradeType.Buy
                ? sym.Bid - ctx.EntryPrice
                : ctx.EntryPrice - sym.Ask;

            return move / ctx.RiskPriceDistance;
        }

        private int BarsSinceEntry(PositionContext ctx)
        {
            int idx = _bot.Bars.OpenTimes.GetIndexByTime(ctx.EntryTime);
            return idx < 0 ? 0 : _bot.Bars.Count - idx;
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
            _bot.Print($"[EXIT] TP2 EXTENDED symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(pos.TradeType == TradeType.Buy ? _bot.Symbols.GetSymbol(pos.SymbolName)?.Bid : _bot.Symbols.GetSymbol(pos.SymbolName)?.Ask)} oldTp={currentTp} newTp={newTp}");
            ctx.LastExtendedTp2 = newTp;
            ctx.Tp2ExtensionMultiplierApplied = desiredR / baseR;
            _bot.Print($"[TTM] TP2 extended from {currentTp} to {newTp}");
        }

    }
}
