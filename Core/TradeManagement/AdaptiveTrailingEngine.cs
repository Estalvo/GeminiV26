using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using GeminiV26.Core;

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

            double oldSl = pos.StopLoss.Value;
            double newSl;
            bool valid;

            switch (decision.TrailingMode)
            {
                case AdaptiveTrailingMode.Structure:
                    valid = TryBuildStructureStop(pos, structure, profile, atr, out newSl);
                    if (!valid)
                    {
                        _bot.Print("[TRAIL][STRUCT] skipped=no confirmed structure");
                        return;
                    }
                    _bot.Print($"[TRAIL][STRUCT] oldSL={oldSl} newSL={newSl}");
                    break;

                case AdaptiveTrailingMode.Liquidity:
                    valid = TryBuildLiquidityStop(pos, structure, profile, atr, out newSl);
                    if (!valid)
                    {
                        _bot.Print("[TRAIL][LIQ] skipped=no liquidityLevel");
                        return;
                    }
                    _bot.Print($"[TRAIL][LIQ] oldSL={oldSl} newSL={newSl}");
                    break;

                default:
                    BuildVolatilityStop(pos, profile, atr, out newSl, out string regime, out double multiplier);
                    _bot.Print($"[TRAIL][VOL] regime={regime} multiplier={multiplier:0.00}");
                    _bot.Print($"[TRAIL][VOL] oldSL={oldSl} newSL={newSl}");
                    break;
            }

            if (ctx.BePrice > 0)
            {
                newSl = pos.TradeType == TradeType.Buy
                    ? Math.Max(newSl, ctx.BePrice)
                    : Math.Min(newSl, ctx.BePrice);
            }

            newSl = Normalize(newSl);

            if (!ImprovesStop(pos, newSl, oldSl))
            {
                _bot.Print("[TRAIL] skipped=no improvement");
                return;
            }

            double minDelta = Math.Max(profile.MinSlUpdateDeltaPips * _bot.Symbol.PipSize, atr * 0.05);
            if (Math.Abs(newSl - oldSl) < minDelta)
            {
                _bot.Print("[TRAIL] skipped=minDelta");
                return;
            }

            if (ctx.LastTrailingStopTarget.HasValue && Math.Abs(ctx.LastTrailingStopTarget.Value - newSl) < (_bot.Symbol.PipSize * 0.1))
            {
                _bot.Print("[TRAIL] skipped=sameTarget");
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

        private bool TryBuildStructureStop(Position pos, StructureSnapshot structure, TrailingProfile profile, double atr, out double newSl)
        {
            double buffer = atr * profile.StructureBufferAtr;
            if (pos.TradeType == TradeType.Buy)
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

                _bot.Print($"[TRAIL][STRUCT] lastSwingLow={anchor}");
                newSl = anchor - buffer;
                return true;
            }

            if (structure.LastLowerHigh == null && structure.LastSwingHigh == null)
            {
                newSl = 0;
                return false;
            }

            double sellAnchor = structure.LastLowerHigh?.Price ?? structure.LastSwingHigh.Price;
            _bot.Print($"[TRAIL][STRUCT] lastSwingHigh={sellAnchor}");
            newSl = sellAnchor + buffer;
            return true;
        }

        private bool TryBuildLiquidityStop(Position pos, StructureSnapshot structure, TrailingProfile profile, double atr, out double newSl)
        {
            double buffer = atr * (profile.StructureBufferAtr * 0.8);

            if (pos.TradeType == TradeType.Buy)
            {
                if (structure.LastSwingLow == null)
                {
                    newSl = 0;
                    return false;
                }

                double liquidityLevel = structure.LastSwingLow.Price;
                _bot.Print($"[TRAIL][LIQ] liquidityLevel={liquidityLevel}");
                newSl = liquidityLevel - buffer;
                return true;
            }

            if (structure.LastSwingHigh == null)
            {
                newSl = 0;
                return false;
            }

            double shortLiquidityLevel = structure.LastSwingHigh.Price;
            _bot.Print($"[TRAIL][LIQ] liquidityLevel={shortLiquidityLevel}");
            newSl = shortLiquidityLevel + buffer;
            return true;
        }

        private void BuildVolatilityStop(Position pos, TrailingProfile profile, double atr, out double newSl, out string regime, out double multiplier)
        {
            double atrPips = atr / _bot.Symbol.PipSize;
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

            var s = _bot.Symbol;
            double price = pos.TradeType == TradeType.Buy ? s.Bid : s.Ask;
            
            newSl = pos.TradeType == TradeType.Buy
                ? price - atr * multiplier
                : price + atr * multiplier;
        }

        private bool ImprovesStop(Position pos, double candidate, double current)
        {
            return pos.TradeType == TradeType.Buy
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
