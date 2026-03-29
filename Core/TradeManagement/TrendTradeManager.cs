using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;

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
        public bool IsRunner { get; init; }
        public double SlAtrMultiplier { get; init; }
        public double TpAtrMultiplier { get; init; }
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

            bool isLong = ctx?.FinalDirection == TradeDirection.Long;
            if (ctx?.FinalDirection == TradeDirection.None)
            {
                GlobalLogger.Log($"[DIR][EXIT_ILLEGAL_SOURCE] source=TradeType posId={position?.Id} reason=missing_final_direction");
                return new TrendDecision
                {
                    State = TradeTrendState.Normal,
                    Score = 0,
                    TrailingMode = AdaptiveTrailingMode.Structure,
                    AllowTp2Extension = false,
                    Tp2ExtensionMultiplier = 1.0,
                    IsRunner = false,
                    SlAtrMultiplier = profile?.LowConfidenceSlAtrMultiplier ?? 1.0,
                    TpAtrMultiplier = profile?.LowConfidenceTpAtrMultiplier ?? 1.0
                };
            }

            bool isRunner = ctx.Tp1Hit;
            if (isRunner && !ctx.RunnerActivated)
            {
                ctx.RunnerActivated = true;
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[TTM] Runner mode activated.", ctx, position));
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[TTM][RUNNER] symbol={position.SymbolName} direction={(isLong ? "LONG" : "SHORT")} pos={position.Id} sl={FormatPrice(position.StopLoss)} tp={FormatPrice(position.TakeProfit)} reason=tp1_hit", ctx, position));
            }

            double priceNow = isLong ? _bot.Symbol.Bid : _bot.Symbol.Ask;
            double move = isLong
                ? priceNow - position.EntryPrice
                : position.EntryPrice - priceNow;

            double currentR = ctx.RiskPriceDistance > 0
                ? move / ctx.RiskPriceDistance
                : 0;

            int score = 0;

            bool impulseStrong = currentR >= 1.2;
            if (impulseStrong)
                score++;

            bool ema21Respected = isLong
                ? priceNow >= _ema21.Result.LastValue
                : priceNow <= _ema21.Result.LastValue;
            if (ema21Respected)
                score++;

            bool ema50Respected = isLong
                ? priceNow >= _ema50.Result.LastValue
                : priceNow <= _ema50.Result.LastValue;
            if (ema50Respected)
                score++;

            bool structureIntact = isLong
                ? structure.LastHigherLow != null
                : structure.LastLowerHigh != null;
            if (structureIntact)
                score++;

            double pullbackDepth = EstimatePullbackDepth(isLong, structure, priceNow);
            bool shallowPullback = atr > 0 && pullbackDepth <= atr * 0.5;
            if (shallowPullback)
                score++;

            TradeTrendState state = score switch
            {
                <= 1 => TradeTrendState.Normal,
                <= 3 => TradeTrendState.Trend,
                _ => TradeTrendState.StrongTrend
            };

            AdaptiveTrailingMode mode = AdaptiveTrailingMode.Structure;

            double slAtrMultiplier = state switch
            {
                TradeTrendState.StrongTrend => profile.StrongTrendSlAtrMultiplier,
                TradeTrendState.Trend => profile.TrendSlAtrMultiplier,
                _ => profile.LowConfidenceSlAtrMultiplier
            };

            double tpAtrMultiplier = state switch
            {
                TradeTrendState.StrongTrend => profile.StrongTrendTpAtrMultiplier,
                TradeTrendState.Trend => profile.TrendTpAtrMultiplier,
                _ => profile.LowConfidenceTpAtrMultiplier
            };

            string confidenceBucket = GetConfidenceBucket(score);
            bool profileStateChanged = !string.Equals(ctx.LastProfileState, state.ToString(), StringComparison.Ordinal);
            bool profileBucketChanged = !string.Equals(ctx.LastProfileBucket, confidenceBucket, StringComparison.Ordinal);

            if (profileStateChanged || profileBucketChanged)
            {
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[TTM][PROFILE] symbol={position.SymbolName} direction={(isLong ? "LONG" : "SHORT")} state={state} score={score} slMult={slAtrMultiplier:0.00} tpMult={tpAtrMultiplier:0.00} reason=confidence_profile", ctx, position));
                ctx.LastProfileState = state.ToString();
                ctx.LastProfileBucket = confidenceBucket;
            }

            bool allowTp2Extension = isRunner && profile.AllowTp2Extension;
            TryExtendTp2(position, ctx, isLong, atr, tpAtrMultiplier, profile, allowTp2Extension);

            bool stateChanged =
                ctx.PostTp1TrendState != state.ToString() ||
                ctx.PostTp1TrendScore != score ||
                ctx.PostTp1TrailingMode != mode.ToString();
            bool valueChanged =
                ctx.LastTtmAllowTp2Extension != allowTp2Extension ||
                !ctx.LastTtmTp2Multiplier.HasValue ||
                Math.Abs(ctx.LastTtmTp2Multiplier.Value - tpAtrMultiplier) >= 0.000001;

            if (stateChanged || valueChanged)
            {
                GlobalLogger.Log(TradeLogIdentity.WithPositionIds($"[TTM] state={state} score={score} mode={mode} allowTp2Ext={allowTp2Extension} mult={tpAtrMultiplier:0.00}", ctx, position));
                ctx.LastTtmAllowTp2Extension = allowTp2Extension;
                ctx.LastTtmTp2Multiplier = tpAtrMultiplier;
            }

            return new TrendDecision
            {
                State = state,
                Score = score,
                TrailingMode = mode,
                AllowTp2Extension = false,
                Tp2ExtensionMultiplier = tpAtrMultiplier,
                IsRunner = isRunner,
                SlAtrMultiplier = slAtrMultiplier,
                TpAtrMultiplier = tpAtrMultiplier
            };
        }

        private void TryExtendTp2(Position pos, PositionContext ctx, bool isLong, double atr, double tpAtrMultiplier, TrailingProfile profile, bool allowTp2Extension)
        {
            string direction = isLong ? "LONG" : "SHORT";
            double currentTp = pos.TakeProfit ?? ctx.Tp2Price ?? 0;

            if (!ctx.Tp1Hit)
            {
                TryLogTp2(ctx, "tp1_not_hit", null, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp=NA delta=0.00000 result=SKIPPED reason=tp1_not_hit");
                return;
            }

            if (!allowTp2Extension)
            {
                TryLogTp2(ctx, "extension_disabled", null, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp=NA delta=0.00000 result=SKIPPED reason=extension_disabled");
                return;
            }

            if (currentTp <= 0 || atr <= 0)
            {
                TryLogTp2(ctx, "missing_tp_or_atr", null, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp=NA delta=0.00000 result=SKIPPED reason=missing_tp_or_atr");
                return;
            }

            double livePrice = isLong ? _bot.Symbol.Ask : _bot.Symbol.Bid;
            double candidateTp = isLong
                ? _bot.Symbol.Ask + (atr * tpAtrMultiplier)
                : _bot.Symbol.Bid - (atr * tpAtrMultiplier);

            double minDelta = atr * profile.Tp2MinDeltaAtr;
            bool outward = isLong
                ? candidateTp > currentTp + minDelta
                : candidateTp < currentTp - minDelta;
            double delta = candidateTp - currentTp;

            if (!outward)
            {
                TryLogTp2(ctx, "not_outward_enough", candidateTp, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp={FormatPrice(candidateTp)} livePrice={FormatPrice(livePrice)} sl={FormatPrice(pos.StopLoss)} delta={delta:0.00000} result=SKIPPED reason=not_outward_enough minDelta={minDelta:0.00000}");
                return;
            }

            double normalizedTp = Normalize(candidateTp);
            if (ctx.LastExtendedTp2.HasValue && Math.Abs(ctx.LastExtendedTp2.Value - normalizedTp) < _bot.Symbol.PipSize)
            {
                TryLogTp2(ctx, "already_extended", normalizedTp, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp={FormatPrice(normalizedTp)} livePrice={FormatPrice(livePrice)} sl={FormatPrice(pos.StopLoss)} delta={delta:0.00000} result=SKIPPED reason=already_extended minDelta={minDelta:0.00000}");
                return;
            }

            var result = _bot.ModifyPosition(pos, pos.StopLoss, normalizedTp);
            if (!result.IsSuccessful)
            {
                TryLogTp2(ctx, "modify_failed", normalizedTp, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp={FormatPrice(normalizedTp)} livePrice={FormatPrice(livePrice)} sl={FormatPrice(pos.StopLoss)} delta={delta:0.00000} result=SKIPPED reason=modify_failed error={result.Error}");
                return;
            }

            ctx.LastExtendedTp2 = normalizedTp;
            if (ctx.RiskPriceDistance > 0 && ctx.Tp2R > 0)
            {
                double desiredR = Math.Abs(normalizedTp - pos.EntryPrice) / ctx.RiskPriceDistance;
                ctx.Tp2ExtensionMultiplierApplied = desiredR / ctx.Tp2R;
            }

            TryLogTp2(ctx, "extended", normalizedTp, $"[TTM][TP2] symbol={pos.SymbolName} direction={direction} currentTp={FormatPrice(currentTp)} candidateTp={FormatPrice(normalizedTp)} livePrice={FormatPrice(livePrice)} sl={FormatPrice(pos.StopLoss)} delta={delta:0.00000} result=EXTENDED minDelta={minDelta:0.00000}");
        }

        private void TryLogTp2(PositionContext ctx, string state, double? value, string message)
        {
            if (ctx == null)
                return;

            double epsilon = GetLogEpsilon();
            bool stateChanged = !string.Equals(ctx.LastTp2State, state, StringComparison.Ordinal);
            bool valueChanged =
                (ctx.LastTp2Value.HasValue != value.HasValue) ||
                (ctx.LastTp2Value.HasValue && value.HasValue && Math.Abs(ctx.LastTp2Value.Value - value.Value) >= epsilon);

            if (!stateChanged && !valueChanged)
                return;

            ctx.LastTp2State = state;
            ctx.LastTp2Value = value;
            GlobalLogger.Log(TradeLogIdentity.WithPositionIds(message, ctx));
        }

        private string GetConfidenceBucket(int score)
        {
            return score switch
            {
                <= 1 => "LOW",
                <= 3 => "MEDIUM",
                _ => "HIGH"
            };
        }

        private double GetLogEpsilon()
        {
            if (_bot.Symbol.TickSize > 0)
                return _bot.Symbol.TickSize;

            if (_bot.Symbol.PipSize > 0)
                return _bot.Symbol.PipSize;

            return double.Epsilon;
        }

        private static double EstimatePullbackDepth(bool isLong, StructureSnapshot structure, double priceNow)
        {
            if (isLong)
            {
                if (structure.LastSwingHigh == null)
                    return 0;

                return Math.Max(0, structure.LastSwingHigh.Price - priceNow);
            }

            if (structure.LastSwingLow == null)
                return 0;

            return Math.Max(0, priceNow - structure.LastSwingLow.Price);
        }

        private double Normalize(double price)
        {
            var s = _bot.Symbol;
            double steps = Math.Round(price / s.TickSize);
            return Math.Round(steps * s.TickSize, s.Digits);
        }

        private static string FormatPrice(double? price)
        {
            return price.HasValue ? price.Value.ToString("0.#####") : "NA";
        }
    }
}
