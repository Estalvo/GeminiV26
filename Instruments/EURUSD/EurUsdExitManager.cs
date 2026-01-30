using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeminiV26.Instruments.EURUSD
{
    public class EurUsdExitManager
    {
        private readonly Robot _bot;

        // PositionId → Context
        private readonly Dictionary<long, PositionContext> _contexts = new();

        // =========================
        // PARAMÉTEREK
        // =========================

        // TP1 után BE +10% R
        private const double BeOffsetR = 0.10;

        // ATR trailing multiplikátorok
        private const double AtrMultTight = 0.8;
        private const double AtrMultNormal = 1.4;
        private const double AtrMultLoose = 2.0;

        public EurUsdExitManager(Robot bot)
        {
            _bot = bot;
        }

        // TradeCore hívja entry után
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

            int bars = BarsSinceEntry(ctx);
            if (bars > 2)
                return;

            double currentR = GetCurrentR(pos, ctx);
            ctx.MaxFavorableR = Math.Max(ctx.MaxFavorableR, currentR);

            bool momentumFail =
                ctx.FinalConfidence < 70 &&
                ctx.MaxFavorableR < 0.1 &&
                currentR < -0.05;

            if (!momentumFail)
                return;

            _bot.ClosePosition(pos);
            ctx.ExitReason = ExitReason.MomentumFail;

            _bot.Print(
                $"[EURUSD EXIT] MOMENTUM FAIL bars={bars} R={currentR:F2} MFE={ctx.MaxFavorableR:F2}"
            );
        }

        // =====================================================
        // TICK-LEVEL EXIT: TP1 / BE / TRAILING
        // =====================================================
        public void OnTick()
        {
            foreach (var ctx in _contexts.Values)
            {
                var pos = _bot.Positions.FirstOrDefault(p => p.Id == ctx.PositionId);
                if (pos == null || !pos.StopLoss.HasValue)
                    continue;

                double rDist = ctx.RiskPriceDistance;
                if (rDist <= 0)
                    continue;

                // =========================
                // TP1 ELŐTTI LOGIKA
                // =========================
                if (!ctx.Tp1Hit)
                {
                    // ---------- EARLY EXIT ----------
                    double currentR = GetCurrentR(pos, ctx);

                    // MFE frissítés (kötelező)
                    ctx.MaxFavorableR = Math.Max(ctx.MaxFavorableR, currentR);

                    bool earlyExit =
                        ctx.FinalConfidence < 65 &&
                        ctx.MaxFavorableR < 0.2 &&
                        BarsSinceEntry(ctx) >= 8;

                    if (earlyExit)
                    {
                        _bot.ClosePosition(pos);
                        ctx.ExitReason = ExitReason.EarlyExit;

                        _bot.Print(
                            $"[EURUSD EXIT] EARLY EXIT " +
                            $"conf={ctx.FinalConfidence} mfeR={ctx.MaxFavorableR:F2}"
                        );

                        continue;
                    }

                    // ---------- TP1 ----------
                    if (CheckTp1Hit(pos, rDist, ctx.Tp1R))
                    {
                        ExecuteTp1(pos, ctx);

                        ctx.Tp1Hit = true;
                        ctx.BeMode = BeMode.AfterTp1;

                        if (ctx.FinalConfidence >= 80)
                        {
                            ApplyBreakEven(pos, ctx, rDist * 0.7);
                            ctx.TrailingMode = TrailingMode.Loose;
                        }
                        else
                        {
                            ApplyBreakEven(pos, ctx, rDist);
                            ctx.TrailingMode = TrailingMode.Tight;
                        }

                        continue;
                    }

                    continue;
                }

                // =========================
                // TRAILING (TP1 UTÁN)
                // =========================
                ApplyTrailing(pos, ctx);
            }
        }

        // =====================================================
        // TP1 CHECK
        // =====================================================
        private bool CheckTp1Hit(Position pos, double rDist, double tp1R)
        {
            return pos.TradeType == TradeType.Buy
                ? _bot.Symbol.Bid >= pos.EntryPrice + rDist * tp1R
                : _bot.Symbol.Ask <= pos.EntryPrice - rDist * tp1R;
        }

        // =====================================================
        // TP1 EXECUTION (partial close)
        // =====================================================
        private void ExecuteTp1(Position pos, PositionContext ctx)
        {
            double fraction = ctx.Tp1CloseFraction > 0 && ctx.Tp1CloseFraction < 1
                ? ctx.Tp1CloseFraction
                : 0.5;

            double minUnits = _bot.Symbol.VolumeInUnitsMin;
            double closeUnits = _bot.Symbol.NormalizeVolumeInUnits(pos.VolumeInUnits * fraction);

            if (closeUnits < minUnits)
                return;

            if (closeUnits >= pos.VolumeInUnits)
                closeUnits = _bot.Symbol.NormalizeVolumeInUnits(pos.VolumeInUnits - minUnits);

            if (closeUnits < minUnits)
                return;

            _bot.ClosePosition(pos, closeUnits);
        }

        // =====================================================
        // BREAK EVEN
        // =====================================================
        private void ApplyBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            double bePrice = pos.TradeType == TradeType.Buy
                ? pos.EntryPrice + rDist * BeOffsetR
                : pos.EntryPrice - rDist * BeOffsetR;

            bool improve = pos.TradeType == TradeType.Buy
                ? bePrice > pos.StopLoss.Value
                : bePrice < pos.StopLoss.Value;

            if (!improve)
                return;

            _bot.ModifyPosition(pos, Normalize(bePrice), pos.TakeProfit);

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;
        }

        // =====================================================
        // TRAILING – FX FIXED
        // =====================================================
        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            var atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            double atrVal = atr.Result.LastValue;
            if (atrVal <= 0)
                return;

            double mult = GetAtrMultiplier(ctx.TrailingMode);
            double trailDist = atrVal * mult;

            double desiredSl = pos.TradeType == TradeType.Buy
                ? _bot.Symbol.Bid - trailDist
                : _bot.Symbol.Ask + trailDist;

            // BE padló
            if (ctx.BePrice > 0)
            {
                desiredSl = pos.TradeType == TradeType.Buy
                    ? Math.Max(desiredSl, ctx.BePrice)
                    : Math.Min(desiredSl, ctx.BePrice);
            }

            desiredSl = Normalize(desiredSl);

            // FIRST TRAIL: ha még BE-n vagyunk → improve nélkül
            bool slAtBe =
                ctx.BePrice > 0 &&
                Math.Abs(pos.StopLoss.Value - ctx.BePrice) <= _bot.Symbol.PipSize;

            if (slAtBe)
            {
                bool better = pos.TradeType == TradeType.Buy
                    ? desiredSl > pos.StopLoss.Value
                    : desiredSl < pos.StopLoss.Value;

                if (better)
                    _bot.ModifyPosition(pos, desiredSl, pos.TakeProfit);

                return;
            }

            // NORMAL TRAILING
            double minImprove = Math.Max(
                _bot.Symbol.PipSize * 0.5,
                atrVal * 0.05
            );

            bool improve = pos.TradeType == TradeType.Buy
                ? desiredSl > pos.StopLoss.Value + minImprove
                : desiredSl < pos.StopLoss.Value - minImprove;

            if (!improve)
                return;

            _bot.ModifyPosition(pos, desiredSl, pos.TakeProfit);
        }

        private static double GetAtrMultiplier(TrailingMode mode)
        {
            return mode switch
            {
                TrailingMode.Tight => AtrMultTight,
                TrailingMode.Normal => AtrMultNormal,
                TrailingMode.Loose => AtrMultLoose,
                _ => AtrMultNormal
            };
        }

        private double Normalize(double price)
        {
            var s = _bot.Symbol;
            double steps = Math.Round(price / s.TickSize);
            return Math.Round(steps * s.TickSize, s.Digits);
        }

        private double GetCurrentR(Position position, PositionContext ctx)
        {
            double priceMove =
                position.TradeType == TradeType.Buy
                    ? _bot.Symbol.Bid - ctx.EntryPrice
                    : ctx.EntryPrice - _bot.Symbol.Ask;

            return priceMove / ctx.RiskPriceDistance;
        }

        private int BarsSinceEntry(PositionContext ctx)
        {
            int entryIndex = _bot.Bars.OpenTimes.GetIndexByTime(ctx.EntryTime);
            if (entryIndex < 0)
                return 0;

            return _bot.Bars.Count - entryIndex;
        }
    }
}
