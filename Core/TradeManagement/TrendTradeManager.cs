using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;

namespace GeminiV26.Core.TradeManagement
{
    public enum TradeTrendState
    {
        Normal,
        Trend,
        StrongTrend
    }

    public enum AdaptiveTrailingMode
    {
        Structure,
        Volatility,
        Liquidity
    }

    public sealed class TrendDecision
    {
        public TradeTrendState State { get; init; }
        public int Score { get; init; }
        public AdaptiveTrailingMode TrailingMode { get; init; }
        public bool AllowTp2Extension { get; init; }
        public double Tp2ExtensionMultiplier { get; init; }
    }

    public sealed class TrendTradeManager
    {
        private readonly Robot _bot;
        private readonly AverageTrueRange _atr;
        private readonly ExponentialMovingAverage _ema21;
        private readonly ExponentialMovingAverage _ema50;

        public TrendTradeManager(Robot bot, Bars bars)
        {
            _bot = bot;
            _atr = _bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential);
            _ema21 = _bot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 21);
            _ema50 = _bot.Indicators.ExponentialMovingAverage(bars.ClosePrices, 50);
        }

        public TrendDecision Evaluate(Position position, PositionContext ctx, TrailingProfile profile, StructureSnapshot structure)
        {
            double atr = _atr.Result.LastValue;
            if (atr <= 0)
                atr = Math.Abs(_bot.Symbol.Bid - _bot.Symbol.Ask) * 4.0;

            double priceNow = position.TradeType == TradeType.Buy ? _bot.Symbol.Bid : _bot.Symbol.Ask;
            double move = position.TradeType == TradeType.Buy
                ? priceNow - position.EntryPrice
                : position.EntryPrice - priceNow;

            double currentR = ctx.RiskPriceDistance > 0
                ? move / ctx.RiskPriceDistance
                : 0;

            int score = 0;

            bool impulseStrong = currentR >= 1.2;
            if (impulseStrong)
                score++;

            bool ema21Respected = position.TradeType == TradeType.Buy
                ? priceNow >= _ema21.Result.LastValue
                : priceNow <= _ema21.Result.LastValue;
            if (ema21Respected)
                score++;

            bool ema50Respected = position.TradeType == TradeType.Buy
                ? priceNow >= _ema50.Result.LastValue
                : priceNow <= _ema50.Result.LastValue;
            if (ema50Respected)
                score++;

            bool structureIntact = position.TradeType == TradeType.Buy
                ? structure.LastHigherLow != null
                : structure.LastLowerHigh != null;
            if (structureIntact)
                score++;

            double pullbackDepth = EstimatePullbackDepth(position, structure, priceNow);
            bool shallowPullback = atr > 0 && pullbackDepth <= atr * 0.5;
            if (shallowPullback)
                score++;

            TradeTrendState state = score switch
            {
                <= 1 => TradeTrendState.Normal,
                <= 3 => TradeTrendState.Trend,
                _ => TradeTrendState.StrongTrend
            };

            AdaptiveTrailingMode mode = state switch
            {
                TradeTrendState.Normal => AdaptiveTrailingMode.Volatility,
                TradeTrendState.Trend => AdaptiveTrailingMode.Structure,
                TradeTrendState.StrongTrend when profile.AllowLiquidityTrailing => AdaptiveTrailingMode.Liquidity,
                _ => AdaptiveTrailingMode.Structure
            };

            bool allowTp2Extension = profile.AllowTp2Extension && state != TradeTrendState.Normal;
            double tp2Multiplier = state switch
            {
                TradeTrendState.Trend => profile.TrendTp2ExtensionMultiplier,
                TradeTrendState.StrongTrend => profile.StrongTrendTp2ExtensionMultiplier,
                _ => 1.0
            };

            _bot.Print($"[TTM] state={state} score={score} mode={mode}");
            if (allowTp2Extension)
                _bot.Print($"[TTM] TP2 extension allowed multiplier={tp2Multiplier:0.00}");

            return new TrendDecision
            {
                State = state,
                Score = score,
                TrailingMode = mode,
                AllowTp2Extension = allowTp2Extension,
                Tp2ExtensionMultiplier = tp2Multiplier
            };
        }

        private static double EstimatePullbackDepth(Position position, StructureSnapshot structure, double priceNow)
        {
            if (position.TradeType == TradeType.Buy)
            {
                if (structure.LastSwingHigh == null)
                    return 0;

                return Math.Max(0, structure.LastSwingHigh.Price - priceNow);
            }

            if (structure.LastSwingLow == null)
                return 0;

            return Math.Max(0, priceNow - structure.LastSwingLow.Price);
        }
    }
}
