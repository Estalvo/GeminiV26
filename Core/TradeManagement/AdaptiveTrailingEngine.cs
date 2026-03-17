using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.TradeManagement
{
    public sealed class AdaptiveTrailingEngine
    {
        private readonly Robot _bot;
        private readonly AverageTrueRange _atr;

        public AdaptiveTrailingEngine(Robot bot)
        {
            _bot = bot;
            _atr = _bot.Indicators.AverageTrueRange(_bot.Bars, 14, MovingAverageType.Exponential);
        }

        public void Apply(Position pos, PositionContext ctx, TrendDecision decision, StructureSnapshot structure, TrailingProfile profile)
        {
            if (!pos.StopLoss.HasValue)
                return;

            double atr = _atr.Result.LastValue;
            if (atr <= 0)
                return;

            if (ctx?.FinalDirection == TradeDirection.None)
            {
                _bot.Print($"[DIR][EXIT_ILLEGAL_SOURCE] source=TradeType posId={pos?.Id} reason=missing_final_direction");
                return;
            }

            bool isLong = ctx.FinalDirection == TradeDirection.Long;
            double oldSl = pos.StopLoss.Value;
            double newSl;
            bool valid = true;

            switch (decision.TrailingMode)
            {
                case AdaptiveTrailingMode.Structure:
                    valid = TryBuildStructureStop(isLong, structure, profile, atr, out newSl);
                    if (!valid)
                        return;
                    break;

                case AdaptiveTrailingMode.Liquidity:
                    valid = TryBuildLiquidityStop(isLong, structure, profile, atr, out newSl);
                    if (!valid)
                        return;
                    break;

                default:
                    BuildVolatilityStop(isLong, profile, atr, out newSl, out _, out _);
                    break;
            }

            if (ctx.BePrice > 0)
            {
                newSl = isLong
                    ? Math.Max(newSl, ctx.BePrice)
                    : Math.Min(newSl, ctx.BePrice);
            }

            newSl = Normalize(newSl);

            if (newSl <= 0)
                return;
                
            if (!ImprovesStop(isLong, newSl, oldSl))
            {                
                return;
            }

            double minDelta = Math.Max(profile.MinSlUpdateDeltaPips * _bot.Symbol.PipSize, atr * 0.05);
            if (Math.Abs(newSl - oldSl) < minDelta)
            {
                return;
            }

            if (ctx.LastTrailingStopTarget.HasValue && Math.Abs(ctx.LastTrailingStopTarget.Value - newSl) < (_bot.Symbol.PipSize * 0.1))
            {
                return;
            }

            var result = _bot.ModifyPosition(pos, newSl, pos.TakeProfit);

            if (!result.IsSuccessful)
            {
                _bot.Print($"[TRAIL] modify FAILED pos={pos.Id} error={result.Error}");
                return;
            }

            ctx.LastTrailingStopTarget = newSl;
            ctx.LastStopLossPrice = newSl;
            _bot.Print($"[TRAIL] modified pos={pos.Id} oldSL={oldSl} newSL={newSl}");
        }

        private bool TryBuildStructureStop(bool isLong, StructureSnapshot structure, TrailingProfile profile, double atr, out double newSl)
        {
            double buffer = atr * profile.StructureBufferAtr;
            if (isLong)
            {
                if (structure.LastHigherLow == null && structure.LastSwingLow == null)
                {
                    newSl = 0;
                    return false;
                }

                double anchor;

                if (structure.LastHigherLow != null)
                    anchor = structure.LastHigherLow.Price;
                else if (structure.LastSwingLow != null)
                    anchor = structure.LastSwingLow.Price;
                else
                {
                    newSl = 0;
                    return false;
                }
                
                newSl = anchor - buffer;
                return true;
            }

            if (structure.LastLowerHigh == null && structure.LastSwingHigh == null)
            {
                newSl = 0;
                return false;
            }

            double sellAnchor;

            if (structure.LastLowerHigh != null)
                sellAnchor = structure.LastLowerHigh.Price;
            else if (structure.LastSwingHigh != null)
                sellAnchor = structure.LastSwingHigh.Price;
            else
            {
                newSl = 0;
                return false;
            }

            newSl = sellAnchor + buffer;
            return true;
        }

        private bool TryBuildLiquidityStop(bool isLong, StructureSnapshot structure, TrailingProfile profile, double atr, out double newSl)
        {
            double buffer = atr * (profile.StructureBufferAtr * 0.8);

            if (isLong)
            {
                if (structure.LastSwingLow == null)
                {
                    newSl = 0;
                    return false;
                }

                double liquidityLevel = structure.LastSwingLow.Price;
                
                newSl = liquidityLevel - buffer;
                return true;
            }

            if (structure.LastSwingHigh == null)
            {
                newSl = 0;
                return false;
            }

            double shortLiquidityLevel = structure.LastSwingHigh.Price;
          
            newSl = shortLiquidityLevel + buffer;
            return true;
        }

        private void BuildVolatilityStop(bool isLong, TrailingProfile profile, double atr, out double newSl, out string regime, out double multiplier)
        {
            var s = _bot.Symbol;
            double atrPips = atr / s.PipSize;

            if (atrPips < 15)
            {
                regime = "Low";
                multiplier = profile.AtrMultiplierLowVol;
            }
            else if (atrPips > 45)
            {
                regime = "High";
                multiplier = profile.AtrMultiplierHighVol;
            }
            else
            {
                regime = "Normal";
                multiplier = profile.AtrMultiplierNormal;
            }
            
            double price = isLong ? s.Bid : s.Ask;
            
            newSl = isLong
                ? price - atr * multiplier
                : price + atr * multiplier;
        }

        private bool ImprovesStop(bool isLong, double candidate, double current)
        {
            return isLong
                ? candidate > current
                : candidate < current;
        }

        private double Normalize(double price)
        {
            var s = _bot.Symbol;
            double steps = Math.Round(price / s.TickSize);
            return Math.Round(steps * s.TickSize, s.Digits);
        }
    }
}
