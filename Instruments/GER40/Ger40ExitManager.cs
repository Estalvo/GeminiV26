using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Interfaces;
using GeminiV26.Data;
using GeminiV26.Data.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeminiV26.Instruments.GER40
{
    public class Ger40ExitManager : IExitManager
    {
        private readonly Robot _bot;
        private readonly EventLogger _eventLogger;

        // PositionId -> PositionContext
        private readonly Dictionary<long, PositionContext> _contexts = new();

        public Ger40ExitManager(Robot bot)
        {
            _bot = bot;
            _eventLogger = new EventLogger(bot.SymbolName);
        }

        public void RegisterContext(PositionContext ctx)
        {
            _contexts[ctx.PositionId] = ctx;
        }

        // =====================================================
        // BAR-LEVEL – Trade Viability Monitor ONLY
        // =====================================================
        public void OnBar(Position pos)
        {
            if (!_contexts.TryGetValue(pos.Id, out var ctx))
                return;

            TryEarlyExit(pos, ctx);
        }

        [Obsolete("Legacy wrapper. TradeCore should call OnBar(Position).")]
        public void Manage(Position pos) => OnBar(pos);

        // =====================================================
        // TICK-LEVEL – TP1 / BE / TRAILING
        // =====================================================
        public void OnTick()
        {
            foreach (var kv in _contexts.ToList())
            {
                long positionId = kv.Key;
                var ctx = kv.Value;

                var pos = _bot.Positions.FirstOrDefault(p => p.Id == positionId);
                if (pos == null)
                {
                    _contexts.Remove(positionId);
                    continue;
                }

                if (!pos.StopLoss.HasValue)
                    continue;

                double rDist = Math.Abs(pos.EntryPrice - pos.StopLoss.Value);
                if (rDist <= 0)
                    continue;

                // =========================
                // TP1
                // =========================
                if (!ctx.Tp1Hit)
                {
                    if (CheckTp1Hit(pos, ctx, rDist))
                    {
                        ExecuteTp1(pos, ctx, rDist);
                        ctx.Tp1Hit = true;
                        continue;
                    }

                    continue; // TP1 előtt nincs trailing
                }

                // =========================
                // TRAILING (TP1 UTÁN)
                // =========================
                ApplyTrailing(pos, ctx);
            }
        }

        // =====================================================
        // TRADE VIABILITY MONITOR
        // =====================================================
        private bool TryEarlyExit(Position pos, PositionContext ctx)
        {
            if (ctx.Tp1Hit)
                return false;

            double slDist = ctx.RiskPriceDistance;
            if (slDist <= 0)
                return false;

            double rNow =
                pos.TradeType == TradeType.Buy
                    ? (pos.Symbol.Bid - pos.EntryPrice) / slDist
                    : (pos.EntryPrice - pos.Symbol.Ask) / slDist;

            // magas confidence → nincs TVM
            if (ctx.FinalConfidence >= 75)
                return false;

            // közepes confidence → nagyobb tolerancia
            if (ctx.FinalConfidence >= 60 && rNow > -0.35)
                return false;

            // alacsony confidence → szigorúbb
            if (rNow < -0.20)
            {
                _eventLogger.Log(new EventRecord
                {
                    EventTimestamp = _bot.Server.Time,
                    Symbol = pos.SymbolName,
                    EventType = "TVM_EarlyExit",
                    PositionId = pos.Id,
                    Confidence = ctx.FinalConfidence,
                    Reason = "R<-0.20"
                });

                _bot.ClosePosition(pos);
                return true;
            }

            return false;
        }

        // =====================================================
        // TP1 CORE (CTX-ALAPÚ)
        // =====================================================
        private bool CheckTp1Hit(Position pos, PositionContext ctx, double rDist)
        {   
            if (ctx.Tp1R <= 0)
                return false;

            return pos.TradeType == TradeType.Buy
                ? _bot.Symbol.Bid >= pos.EntryPrice + rDist * ctx.Tp1R
                : _bot.Symbol.Ask <= pos.EntryPrice - rDist * ctx.Tp1R;
        }

        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            double closeVol =
                _bot.Symbol.NormalizeVolumeInUnits(
                    pos.VolumeInUnits * ctx.Tp1CloseFraction,
                    RoundingMode.Down
                );

            if (closeVol >= _bot.Symbol.VolumeInUnitsMin)
                _bot.ClosePosition(pos, closeVol);

            ctx.Tp1ClosedVolumeInUnits = closeVol;
            ctx.RemainingVolumeInUnits =
                Math.Max(0, pos.VolumeInUnits - closeVol);

            ApplyBreakEven(pos, ctx, rDist);
        }

        // =====================================================
        // BREAK EVEN (CTX-ALAPÚ)
        // =====================================================
        private void ApplyBreakEven(Position pos, PositionContext ctx, double rDist)
        {
            // ❌ ctx.BePrice.HasValue helyett
            if (ctx.BePrice > 0)
                return;

            double bePrice =
                pos.TradeType == TradeType.Buy
                    ? pos.EntryPrice + rDist * ctx.BeOffsetR
                    : pos.EntryPrice - rDist * ctx.BeOffsetR;

            ctx.BePrice = bePrice;

            if (pos.StopLoss.HasValue)
            {
                bool improve =
                    pos.TradeType == TradeType.Buy
                        ? bePrice > pos.StopLoss.Value
                        : bePrice < pos.StopLoss.Value;

                if (!improve)
                    return;
            }

            // ❌ pos.TakeProfit (double?) helyett
            _bot.ModifyPosition(
                pos,
                Normalize(bePrice),
                pos.TakeProfit ?? 0
            );

            _eventLogger.Log(new EventRecord
            {
                EventTimestamp = _bot.Server.Time,
                Symbol = _bot.SymbolName,
                EventType = "BreakEvenSet",
                PositionId = pos.Id,
                Confidence = ctx.FinalConfidence,
                Reason = "BE_SET"
            });
        }

        // =====================================================
        // TRAILING (TP1 UTÁN) – FIXED
        // =====================================================
        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            if (!pos.StopLoss.HasValue)
                return;

            var bars = _bot.Bars;
            if (bars.Count < 3)
                return;

            double close0 = bars.ClosePrices.Last(0);
            double close1 = bars.ClosePrices.Last(1);
            double close2 = bars.ClosePrices.Last(2);

            bool progress =
                pos.TradeType == TradeType.Buy
                    ? close0 > close1 && close1 > close2
                    : close0 < close1 && close1 < close2;

            if (!progress)
                return;

            double sl = pos.StopLoss.Value;
            if (sl <= 0)
                return;

            double tp2 = ctx.Tp2Price ?? pos.TakeProfit ?? 0;
            if (tp2 <= 0)
                return;

            double trailFrac =
                ctx.TrailingMode == TrailingMode.Loose ? 0.18 :
                ctx.TrailingMode == TrailingMode.Normal ? 0.12 :
                                                          0.08;

            double dist = Math.Abs(tp2 - sl) * trailFrac;

            double newSl =
                pos.TradeType == TradeType.Buy
                    ? _bot.Symbol.Bid - dist
                    : _bot.Symbol.Ask + dist;

            bool improve =
                pos.TradeType == TradeType.Buy
                    ? newSl > sl
                    : newSl < sl;

            if (!improve)
                return;

            double normNewSl = Normalize(newSl);

            // 🔒 broker-safe guard: normalizálás után is javuljon
            if (Math.Abs(normNewSl - sl) < _bot.Symbol.TickSize)
                return;

            _bot.ModifyPosition(
                pos,
                normNewSl,
                pos.TakeProfit
            );

            _eventLogger.Log(new EventRecord
            {
                EventTimestamp = _bot.Server.Time,
                Symbol = _bot.SymbolName,
                EventType = "EXIT_TRAILING",
                PositionId = pos.Id,
                Confidence = ctx.FinalConfidence,
                Reason = "TrailingStep",
                Extra = "GER40 ExitManager"
            });
        }

        // =====================================================
        // PRICE NORMALIZATION (INDEX SAFE)
        // =====================================================
        private double Normalize(double price)
        {
            var s = _bot.Symbol;
            double steps = Math.Round(price / s.TickSize);
            return Math.Round(steps * s.TickSize, s.Digits);
        }
    }
}
