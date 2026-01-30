using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;

namespace GeminiV26.Instruments.BTCUSD
{
    /// <summary>
    /// BTCUSD ExitManager – NAS100 struktúra alapján, de crypto-safe (double volume step).
    /// Szabály: TP1 előtt NINCS trailing. TP1 után BE + trailing.
    /// </summary>

    public class BtcUsdExitManager
    {
        private readonly Robot _bot;

        // PositionId -> context
        private readonly Dictionary<long, PositionContext> _contexts = new();

        // ===== TP1 / BE =====        
        private const double BeOffsetR = 0.05;

        // ===== Trailing =====
        // Crypto ATR trail általában nagyobb multipliert igényel
        private const double MinTrailImprovePips = 30;

        private const double TrailTight = 1.2;
        private const double TrailNormal = 1.8;
        private const double TrailLoose = 2.5;

        private const int AtrPeriod = 14;
        private readonly AverageTrueRange _atrM5;

        public BtcUsdExitManager(Robot bot)
        {
            _bot = bot;
            _atrM5 = _bot.Indicators.AverageTrueRange(
                AtrPeriod,
                MovingAverageType.Exponential
            );
        }

        // =========================================================
        // CONTEXT REGISZTRÁCIÓ
        // Executor hívja meg sikeres pozíciónyitás után
        // =========================================================
        public void RegisterContext(PositionContext ctx)
        {
            _contexts[Convert.ToInt64(ctx.PositionId)] = ctx;
        }

        // =========================================================
        // BAR-LEVEL EXIT MANAGEMENT
        // Kötelező a közös IExitManager szerződés miatt,
        // BTC esetén jelenleg nincs bar-alapú exit logika
        // =========================================================
        public void OnBar(Position position)
        {
            // szándékosan üres
        }

        // ==============================
        // TICK-LEVEL EXIT MANAGEMENT
        // ==============================
        // TP1 + trailing IDE KERÜLT,
        // hogy tick-pontos végrehajtás legyen
        public void OnTick()
        {
            foreach (var kv in _contexts)
            {
                long key = kv.Key;
                var ctx = kv.Value;

                Position pos = null;
                foreach (var p in _bot.Positions)
                {
                    if (p.Id == kv.Key)
                    {
                        pos = p;
                        break;
                    }
                }

                if (pos == null)
                    continue;

                if (!pos.StopLoss.HasValue)
                    continue;

                double stopLoss = pos.StopLoss.Value;

                // R-distance az eredeti SL alapján (ctx.EntryPrice vs aktuális SL)
                double rDist = ctx.RiskPriceDistance;
                if (rDist <= 0)
                    continue;

                // =========================
                // TP1 (tick-pontos)
                // =========================
                if (!ctx.Tp1Hit)
                {
                    double tp1Price =
                        ctx.EntryPrice + Direction(pos) * rDist * ctx.Tp1R;

                    bool reached =
                        (pos.TradeType == TradeType.Buy && _bot.Symbol.Bid >= tp1Price) ||
                        (pos.TradeType == TradeType.Sell && _bot.Symbol.Ask <= tp1Price);

                    if (reached)
                    {
                        _bot.Print("[BTCUSD][TP1][HIT] TP1 HIT (OnTick)");
                        ExecuteTp1(pos, ctx, rDist);
                        continue;
                    }

                    // 🔒 TP1 előtt trailing TILOS
                    continue;
                }

                // =========================
                // Trailing (csak TP1 után)
                // =========================
                ApplyTrailing(pos, ctx);
            }
        }

        // =========================================================
        // Manage() MEGMARAD
        // (nem töröljük, de exit logika már OnTick-ben fut)
        // =========================================================
        public void Manage(Position pos)
        {
            _bot.Print(
                $"[BTCUSD][INFO] Manage() called, exit handled in OnTick()");
        }

        private void ExecuteTp1(Position pos, PositionContext ctx, double rDist)
        {
            // 1) Partial close (crypto-safe)
            // NAS logika: ctx.Tp1CloseFraction (ha nincs értelmes, 0.5)
            double frac = ctx.Tp1CloseFraction;
            if (frac <= 0 || frac >= 1)
                frac = 0.5;

            double minUnits = _bot.Symbol.VolumeInUnitsMin;

            // Position.VolumeInUnits lehet double crypto instrumenten
            double targetUnits = pos.VolumeInUnits * frac;

            // Normalize a symbol step-re (crypto: 0.01, 0.001, stb.)
            double closeUnits = _bot.Symbol.NormalizeVolumeInUnits(targetUnits);

            if (closeUnits < minUnits)
                closeUnits = minUnits;

            // Ne zárjuk ki véletlen fullra (maradjon legalább minUnits)
            if (closeUnits >= pos.VolumeInUnits)
                closeUnits = _bot.Symbol.NormalizeVolumeInUnits(
                    pos.VolumeInUnits - minUnits);

            if (closeUnits >= minUnits && closeUnits > 0)
            {
                _bot.ClosePosition(pos, closeUnits);
                ctx.Tp1ClosedVolumeInUnits = closeUnits;
            }

            // 2) TP1 state
            ctx.Tp1Hit = true;

            // 3) BE / protective SL after TP1
            double bePrice =
                ctx.EntryPrice + Direction(pos) * rDist * BeOffsetR;

            // Soha ne rontsunk az SL-en
            if (pos.StopLoss.HasValue)
            {
                bePrice = pos.TradeType == TradeType.Buy
                    ? Math.Max(bePrice, pos.StopLoss.Value)
                    : Math.Min(bePrice, pos.StopLoss.Value);
            }

            // ModifyPosition: SL -> BE, TP marad (TP2)
            _bot.ModifyPosition(pos, bePrice, pos.TakeProfit);

            ctx.BePrice = bePrice;
            ctx.BeMode = BeMode.AfterTp1;

            // 4) Trailing mode (ha nincs beállítva, default Normal)
            if (ctx.TrailingMode == TrailingMode.None)
                ctx.TrailingMode = TrailingMode.Normal;
        }

        private void ApplyTrailing(Position pos, PositionContext ctx)
        {
            if (!pos.StopLoss.HasValue)
                return;

            double atr = _atrM5.Result.LastValue;
            if (atr <= 0)
                return;

            double mult = GetTrailMultiplier(ctx.TrailingMode);
            double trailDist = atr * mult;

            double desiredSl =
                pos.TradeType == TradeType.Buy
                    ? _bot.Symbol.Bid - trailDist
                    : _bot.Symbol.Ask + trailDist;

            // BE floor (TP1 után)
            if (ctx.BePrice > 0)
            {
                desiredSl = pos.TradeType == TradeType.Buy
                    ? Math.Max(desiredSl, ctx.BePrice)
                    : Math.Min(desiredSl, ctx.BePrice);
            }

            double minImprove =
                _bot.Symbol.PipSize * MinTrailImprovePips;

            bool improve =
                (pos.TradeType == TradeType.Buy &&
                 desiredSl > pos.StopLoss.Value + minImprove) ||
                (pos.TradeType == TradeType.Sell &&
                 desiredSl < pos.StopLoss.Value - minImprove);

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
