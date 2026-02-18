using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;
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

        private const double BeOffsetR = 0.10;

        private const double AtrMultTight = 0.8;
        private const double AtrMultNormal = 1.4;
        private const double AtrMultLoose = 2.0;

        public GbpUsdExitManager(Robot bot)
        {
            _bot = bot;
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

            // =========================
            // FX EARLY EXIT TILTÁS
            // =========================
            _bot.Print(
                $"[{ctx.Symbol} EXIT SKIP] FX ExitManager – no momentum exit before TP1"
            );
            return;
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
                    double currentR = GetCurrentR(pos, ctx);
                    ctx.MaxFavorableR = Math.Max(ctx.MaxFavorableR, currentR);

                    if (ctx.FinalConfidence < 65 && ctx.MaxFavorableR < 0.2 && BarsSinceEntry(ctx) >= 8)
                    {
                        _bot.ClosePosition(pos);
                        ctx.ExitReason = ExitReason.EarlyExit;
                        continue;
                    }

                    double tp1Trigger =
                        pos.TradeType == TradeType.Buy
                            ? pos.EntryPrice + rDist * ctx.Tp1R
                            : pos.EntryPrice - rDist * ctx.Tp1R;

                    _bot.Print(
                        $"[TP1 DEBUG] entry={pos.EntryPrice:F5} " +
                        $"rDist={rDist:F5} Tp1R={ctx.Tp1R:F2} " +
                        $"currentR={GetCurrentR(pos, ctx):F2}"
                    );

                    if (CheckTp1Hit(pos, rDist, ctx.Tp1R))
                    {
                        ExecuteTp1(pos, ctx);

                        ctx.Tp1Hit = true;
                        ApplyBreakEven(pos, ctx);
                        ctx.TrailingMode = ctx.FinalConfidence >= 80
                            ? TrailingMode.Loose
                            : TrailingMode.Tight;

                        continue;
                    }

                    continue;
                }

                ApplyTrailing(pos, ctx);
            }
        }

        // =====================================================
        // TP1 CHECK
        // =====================================================
        private bool CheckTp1Hit(Position pos, double rDist, double tp1R)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);

            return pos.TradeType == TradeType.Buy
                ? sym.Bid >= pos.EntryPrice + rDist * tp1R
                : sym.Ask <= pos.EntryPrice - rDist * tp1R;
        }

        // =====================================================
        // TP1 EXECUTION
        // =====================================================
        private void ExecuteTp1(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);

            double fraction = ctx.Tp1CloseFraction > 0 && ctx.Tp1CloseFraction < 1
                ? ctx.Tp1CloseFraction
                : 0.5;

            double minUnits = sym.VolumeInUnitsMin;
            double closeUnits = sym.NormalizeVolumeInUnits(pos.VolumeInUnits * fraction);

            if (closeUnits < minUnits)
                return;

            if (closeUnits >= pos.VolumeInUnits)
                closeUnits = sym.NormalizeVolumeInUnits(pos.VolumeInUnits - minUnits);

            if (closeUnits < minUnits)
                return;

            _bot.ClosePosition(pos, closeUnits);
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
    }
}
