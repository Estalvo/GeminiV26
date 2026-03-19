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
            string direction = isLong ? "LONG" : "SHORT";
            double oldSl = pos.StopLoss.Value;
            double newSl = 0;
            string trailMode = "FALLBACK";
            string reason = string.Empty;
            bool valid = false;
            string structureLog = null;
            string fallbackVolatilityLog = null;
            string fallbackLog = null;
            bool forceFallback = ctx.Tp1Hit
                && !ctx.LastTrailingStopTarget.HasValue
                && ctx.BarsSinceEntryM5 >= profile.ForceVolatilityTrailAfterBars;

            if (!forceFallback)
            {
                valid = TryBuildStructureStop(isLong, structure, profile, atr, decision.SlAtrMultiplier, out newSl, out string structureReason, out int anchorBarsAgo);
                if (valid)
                {
                    trailMode = "STRUCTURE";
                    reason = $"structure_anchor barsAgo={anchorBarsAgo}";
                    structureLog = $"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode=STRUCTURE slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason={reason}";
                }
                else
                {
                    reason = structureReason;
                }
            }
            else
            {
                reason = $"forced_update barsSinceEntry={ctx.BarsSinceEntryM5}";
            }

            if (!valid)
            {
                BuildVolatilityStop(isLong, profile, atr, decision.SlAtrMultiplier, out newSl, out string regime, out double multiplier);
                trailMode = "VOLATILITY_FALLBACK";
                reason = string.IsNullOrWhiteSpace(reason) ? $"fallback regime={regime}" : $"{reason} regime={regime}";
                fallbackVolatilityLog = $"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode=VOLATILITY_FALLBACK slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason={reason} multiplier={multiplier:0.00}";
                fallbackLog = $"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode=FALLBACK slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason={reason}";
            }

            if (trailMode == "VOLATILITY_FALLBACK")
            {
                newSl = isLong
                    ? Math.Max(newSl, oldSl)
                    : Math.Min(newSl, oldSl);
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

            if (!ImprovesStop(isLong, newSl, oldSl) && trailMode == "STRUCTURE")
            {
                BuildVolatilityStop(isLong, profile, atr, decision.SlAtrMultiplier, out double fallbackSl, out string fallbackRegime, out double fallbackMultiplier);
                fallbackSl = isLong
                    ? Math.Max(fallbackSl, oldSl)
                    : Math.Min(fallbackSl, oldSl);

                if (ctx.BePrice > 0)
                {
                    fallbackSl = isLong
                        ? Math.Max(fallbackSl, ctx.BePrice)
                        : Math.Min(fallbackSl, ctx.BePrice);
                }

                fallbackSl = Normalize(fallbackSl);
                fallbackVolatilityLog = $"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode=VOLATILITY_FALLBACK slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(fallbackSl)} tp={FormatPrice(pos.TakeProfit)} reason=structure_static regime={fallbackRegime} multiplier={fallbackMultiplier:0.00}";
                fallbackLog = $"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode=FALLBACK slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(fallbackSl)} tp={FormatPrice(pos.TakeProfit)} reason=structure_static";

                if (ImprovesStop(isLong, fallbackSl, oldSl))
                {
                    newSl = fallbackSl;
                    trailMode = "VOLATILITY_FALLBACK";
                    structureLog = null;
                }
            }

            if (!ImprovesStop(isLong, newSl, oldSl))
            {
                if (!ctx.TrailNoImprovementLogged)
                {
                    ctx.TrailNoImprovementLogged = true;
                    _bot.Print($"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode={trailMode} slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason=no_improvement");
                }
                return;
            }

            double epsilon = GetPriceEpsilon();
            if (Math.Abs(newSl - oldSl) < epsilon)
                return;

            double minDelta = Math.Max(profile.MinSlUpdateDeltaPips * _bot.Symbol.PipSize, atr * 0.05);
            if (Math.Abs(newSl - oldSl) < minDelta)
            {
                _bot.Print($"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode={trailMode} slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason=below_min_delta minDelta={minDelta:0.00000}");
                return;
            }

            if (ctx.LastTrailingStopTarget.HasValue && Math.Abs(ctx.LastTrailingStopTarget.Value - newSl) < epsilon)
            {
                _bot.Print($"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode={trailMode} slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason=duplicate_target");
                return;
            }

            TryLogTrailCandidate(ctx, reason, structureLog, oldSl, newSl, isLong);
            TryLogTrailCandidate(ctx, reason, fallbackVolatilityLog, oldSl, newSl, isLong);
            TryLogTrailCandidate(ctx, reason, fallbackLog, oldSl, newSl, isLong);

            var result = _bot.ModifyPosition(pos, newSl, pos.TakeProfit);

            if (!result.IsSuccessful)
            {
                _bot.Print($"[TRAIL] modify FAILED pos={pos.Id} error={result.Error}");
                _bot.Print($"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode={trailMode} slOld={FormatPrice(oldSl)} slCandidate={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason=modify_failed error={result.Error}");
                return;
            }

            ctx.LastTrailingStopTarget = newSl;
            ctx.LastStopLossPrice = newSl;
            ctx.TrailingActivated = true;
            _bot.Print($"[TRAIL] modified pos={pos.Id} oldSL={oldSl} newSL={newSl}");
            _bot.Print($"[TTM][TRAIL] symbol={pos.SymbolName} direction={direction} mode={trailMode} slOld={FormatPrice(oldSl)} slNew={FormatPrice(newSl)} tp={FormatPrice(pos.TakeProfit)} reason=updated");
            _bot.Print($"[EXIT] TRAILING ACTIVE symbol={pos.SymbolName} positionId={pos.Id} direction={pos.TradeType} currentPrice={(isLong ? _bot.Symbol.Bid : _bot.Symbol.Ask)} sl={newSl} tp={pos.TakeProfit}");
        }

        private bool TryBuildStructureStop(bool isLong, StructureSnapshot structure, TrailingProfile profile, double atr, double slAtrMultiplier, out double newSl, out string reason, out int anchorBarsAgo)
        {
            double buffer = atr * profile.StructureBufferAtr * Math.Max(1.0, slAtrMultiplier / profile.TrendSlAtrMultiplier);
            int lastClosedBarIndex = Math.Max(0, _bot.Bars.Count - 2);

            if (isLong)
            {
                StructurePoint anchor = structure.LastHigherLow ?? structure.LastSwingLow;
                if (anchor == null)
                {
                    newSl = 0;
                    reason = "no_structure_anchor";
                    anchorBarsAgo = int.MaxValue;
                    return false;
                }

                anchorBarsAgo = Math.Max(0, lastClosedBarIndex - anchor.Index);
                if (anchorBarsAgo > profile.StructureFallbackBars)
                {
                    newSl = 0;
                    reason = $"structure_stale barsAgo={anchorBarsAgo}";
                    return false;
                }

                newSl = anchor.Price - buffer;
                reason = "structure_valid";
                return true;
            }

            StructurePoint sellAnchor = structure.LastLowerHigh ?? structure.LastSwingHigh;
            if (sellAnchor == null)
            {
                newSl = 0;
                reason = "no_structure_anchor";
                anchorBarsAgo = int.MaxValue;
                return false;
            }

            anchorBarsAgo = Math.Max(0, lastClosedBarIndex - sellAnchor.Index);
            if (anchorBarsAgo > profile.StructureFallbackBars)
            {
                newSl = 0;
                reason = $"structure_stale barsAgo={anchorBarsAgo}";
                return false;
            }

            newSl = sellAnchor.Price + buffer;
            reason = "structure_valid";
            return true;
        }

        private void BuildVolatilityStop(bool isLong, TrailingProfile profile, double atr, double slAtrMultiplier, out double newSl, out string regime, out double multiplier)
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

            multiplier = slAtrMultiplier;
            double price = isLong ? s.Bid : s.Ask;

            newSl = isLong
                ? price - atr * multiplier
                : price + atr * multiplier;

            if (isLong)
                newSl = Math.Max(newSl, _bot.Symbol.Bid - atr * multiplier);
            else
                newSl = Math.Min(newSl, _bot.Symbol.Ask + atr * multiplier);
        }

        private bool ImprovesStop(bool isLong, double candidate, double current)
        {
            return isLong
                ? candidate > current
                : candidate < current;
        }

        private bool CanLogTrailChange(bool isLong, double oldSl, double candidateSl)
        {
            return ImprovesStop(isLong, candidateSl, oldSl) &&
                   Math.Abs(candidateSl - oldSl) >= GetPriceEpsilon();
        }

        private void TryLogTrailCandidate(PositionContext ctx, string reason, string message, double oldSl, double candidateSl, bool isLong)
        {
            if (string.IsNullOrWhiteSpace(message) || !CanLogTrailChange(isLong, oldSl, candidateSl))
                return;

            if (reason.Contains("no_structure_anchor", StringComparison.Ordinal))
            {
                if (ctx.TrailNoStructureLogged)
                    return;

                ctx.TrailNoStructureLogged = true;
            }

            _bot.Print(message);
        }


        private double GetPriceEpsilon()
        {
            if (_bot.Symbol.TickSize > 0)
                return _bot.Symbol.TickSize;

            if (_bot.Symbol.PipSize > 0)
                return _bot.Symbol.PipSize;

            return double.Epsilon;
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
