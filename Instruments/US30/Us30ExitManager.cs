using System;
using System.Collections.Generic;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;

namespace GeminiV26.Instruments.US30
{
    public class Us30ExitManager
    {
        private readonly Robot _bot;
        private readonly Dictionary<long, PositionContext> _contexts = new();

        // ===== TUNING =====
        private const double BeOffsetR = 0.05;

        private const double TrailTight = 1.2;
        private const double TrailNormal = 1.6;
        private const double TrailLoose = 2.1;

        private const int AtrPeriod = 14;
        private readonly AverageTrueRange _atrM5;

        // Indexhez: ne legyen túl “ideges”
        private const double MinImproveAtrFrac = 0.05;     // 5% ATR
        private const double MinImprovePipsFrac = 0.5;     // 0.5 pip

        public Us30ExitManager(Robot bot)
        {
            _bot = bot;
            _atrM5 = _bot.Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
        }

        public void RegisterContext(PositionContext ctx)
        {
            _contexts[Convert.ToInt64(ctx.PositionId)] = ctx;
        }

        // =====================================================
        // TICK-LEVEL LIFECYCLE (FX-SZERŰ)
        // =====================================================
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

                    if (CheckTp1Hit(pos, rDist, tp1R))
                    {
                        ExecuteTp1(pos, ctx);
                        MoveToBreakEven(pos, ctx, rDist);

                        ctx.Tp1Hit = true;

                        if (ctx.TrailingMode == TrailingMode.None)
                            ctx.TrailingMode = TrailingMode.Normal;

                        return; // <<< KRITIKUS
                    }

                    continue;
                }

                ApplyTrailing(pos, ctx);
            }
        }
                public void OnBar(Position pos)
        {
            // szándékosan üres vagy későbbi TVM-hez használható
        }

        // =====================================================
        // TP1 CHECK
        // =====================================================
        private bool CheckTp1Hit(Position pos, double rDist, double tp1R)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return false;

            double tp1Price = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + rDist * tp1R
                : pos.EntryPrice - rDist * tp1R;

            double priceNow = pos.TradeType == TradeType.Buy
                ? sym.Bid
                : sym.Ask;

            return pos.TradeType == TradeType.Buy
                ? priceNow >= tp1Price
                : priceNow <= tp1Price;
        }

        // =====================================================
        // TP1 EXECUTION
        // =====================================================
         private void ExecuteTp1(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return;

            double frac = ctx.Tp1CloseFraction > 0 && ctx.Tp1CloseFraction < 1
                ? ctx.Tp1CloseFraction
                : 0.5;

            long minUnits = (long)sym.VolumeInUnitsMin;

            long targetUnits = (long)Math.Floor(pos.VolumeInUnits * frac);
            if (targetUnits <= 0)
                return;

            long closeUnits = (long)sym.NormalizeVolumeInUnits(targetUnits);

            if (closeUnits < minUnits)
                return;

            if (closeUnits >= pos.VolumeInUnits)
                closeUnits = (long)(pos.VolumeInUnits - minUnits);

            if (closeUnits <= 0)
                return;

            _bot.ClosePosition(pos, closeUnits);

            ctx.Tp1ClosedVolumeInUnits = closeUnits;
            ctx.RemainingVolumeInUnits = pos.VolumeInUnits - closeUnits;
        }

        // =====================================================
        // BREAK EVEN
        // =====================================================
        private void MoveToBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return;

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
        // =====================================================
        // TRAILING (FX-SZERŰ, INDEX TUNING)
        // =====================================================
        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);
            if (sym == null)
                return;

            double atr = _atrM5.Result.LastValue;
            if (atr <= 0)
                return;

            double mult = GetTrailMultiplier(ctx.TrailingMode);
            double trailDist = atr * mult;

            double desiredSl =
                pos.TradeType == TradeType.Buy
                    ? sym.Bid - trailDist
                    : sym.Ask + trailDist;

            if (ctx.BePrice > 0)
            {
                desiredSl = pos.TradeType == TradeType.Buy
                    ? Math.Max(desiredSl, ctx.BePrice)
                    : Math.Min(desiredSl, ctx.BePrice);
            }

            double minImprove = Math.Max(
                sym.PipSize * MinImprovePipsFrac,
                atr * MinImproveAtrFrac
            );

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
    }
}
