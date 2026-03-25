using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.EntryTypes
{
    internal sealed class DirectionQualityRequest
    {
        public string TypeTag { get; init; }
        public bool ApplyTrendRegimePenalty { get; init; }
    }

    internal static class EntryDirectionQuality
    {
        public static int Apply(
            EntryContext ctx,
            TradeDirection direction,
            int score,
            DirectionQualityRequest request)
        {
            if (ctx == null)
                return score;

            ResolveHtf(ctx, out var instrumentClass, out var htfDirection, out var htfConfidence);

            bool breakoutConfirmed = HasDirectionalBreakout(ctx, direction);
            bool impulseAligned = HasDirectionalImpulse(ctx, direction);
            bool pullbackAligned = HasDirectionalPullback(ctx, direction);
            bool flagAligned = HasDirectionalFlag(ctx, direction);
            bool structureAligned = breakoutConfirmed || flagAligned || pullbackAligned || impulseAligned;
            bool oppositeStructure = HasDirectionalStructure(ctx, Opposite(direction));
            bool momentumAligned = HasDirectionalMomentum(ctx, direction);
            bool regimeCompatible = IsRegimeCompatible(ctx, instrumentClass, direction, request.ApplyTrendRegimePenalty, breakoutConfirmed, impulseAligned, momentumAligned, structureAligned, out var regime);

            int penalty = 0;
            int bonus = 0;
            int marketStateAdditivePenalty = 0;
            double trendScaling = 1.0;
            double momentumScaling = 1.0;
            double comboScaling = 1.0;
            double adxScaling = 1.0;
            double baseScoreFlow = score;
            double afterAdditiveFlow = score;
            double afterTrendFlow = score;
            double afterMomentumFlow = score;
            double afterComboFlow = score;
            double afterAdxFlow = score;

            if (structureAligned)
            {
                bonus += 6;
                if (!oppositeStructure)
                    bonus += 2;
            }
            else
            {
                penalty += instrumentClass == InstrumentClass.FX ? 18 : 14;

                if (request.ApplyTrendRegimePenalty)
                    penalty += 2;
            }

            if (!oppositeStructure && request.ApplyTrendRegimePenalty)
                bonus += 2;

            if (breakoutConfirmed)
                bonus += 4;

            if (!momentumAligned)
            {
                penalty += instrumentClass switch
                {
                    InstrumentClass.CRYPTO => 20,
                    InstrumentClass.INDEX => 16,
                    InstrumentClass.METAL => 14,
                    _ => 12
                };
            }
            else
            {
                bonus += 3;
            }

            if (htfDirection != TradeDirection.None && htfConfidence >= 0.70)
            {
                if (direction != htfDirection)
                    penalty += 30;
                else
                    bonus += 5;
            }

            var logicBias = ctx.LogicBiasDirection;
            var logicConfidence = ctx.LogicBiasConfidence;
            if (logicBias != TradeDirection.None && logicConfidence >= 60)
            {
                if (direction != logicBias)
                    penalty += 12;
                else
                    bonus += 4;
            }

            if (!regimeCompatible)
                penalty += 20;

            if (ctx.MarketState != null)
            {
                var style = ResolveStyle(request?.TypeTag);
                bool noTrend = !ctx.MarketState.IsTrend;
                bool noMomentum = !ctx.MarketState.IsMomentum;
                bool lowEnergy = ctx.MarketState.IsLowVol || !ctx.IsAtrExpanding_M5;

                if (noTrend)
                    marketStateAdditivePenalty += style == EntryStyle.Range ? 1 : 3;

                if (noMomentum)
                    marketStateAdditivePenalty += style == EntryStyle.Range ? 1 : 4;

                if (lowEnergy)
                    marketStateAdditivePenalty += 2;

                if (marketStateAdditivePenalty > 4)
                    marketStateAdditivePenalty = 4;

                switch (style)
                {
                    case EntryStyle.Breakout:
                        trendScaling = noTrend ? 0.90 : 1.00;
                        momentumScaling = noMomentum ? 0.75 : 1.00;
                        break;
                    case EntryStyle.Pullback:
                        trendScaling = noTrend ? 0.75 : 1.00;
                        momentumScaling = noMomentum ? 0.90 : 1.00;
                        break;
                    case EntryStyle.Flag:
                        trendScaling = noTrend ? 0.80 : 1.00;
                        momentumScaling = noMomentum ? 0.80 : 1.00;
                        break;
                    case EntryStyle.Reversal:
                        trendScaling = noTrend ? 0.95 : 1.00;
                        momentumScaling = noMomentum ? 0.88 : 1.00;
                        break;
                    case EntryStyle.Range:
                        trendScaling = noTrend ? 0.98 : 1.00;
                        momentumScaling = noMomentum ? 0.95 : 1.00;
                        break;
                    default:
                        trendScaling = noTrend ? 0.85 : 1.00;
                        momentumScaling = noMomentum ? 0.85 : 1.00;
                        break;
                }

                if (noTrend && noMomentum)
                    comboScaling = (style == EntryStyle.Reversal || style == EntryStyle.Range) ? 0.80 : 0.60;

                double minAdx = instrumentClass switch
                {
                    InstrumentClass.INDEX => 20.0,
                    InstrumentClass.CRYPTO => 20.0,
                    InstrumentClass.FX => 18.0,
                    _ => 18.0
                };

                adxScaling = Math.Clamp(ctx.Adx_M5 / minAdx, 0.65, 1.10);
            }

            if (instrumentClass == InstrumentClass.INDEX &&
                htfDirection != TradeDirection.None &&
                htfConfidence >= 0.70 &&
                logicBias != TradeDirection.None &&
                logicConfidence >= 60 &&
                htfDirection == logicBias &&
                direction != htfDirection)
            {
                penalty += 10;
            }

            if (instrumentClass == InstrumentClass.METAL &&
                request.ApplyTrendRegimePenalty &&
                !structureAligned &&
                !breakoutConfirmed)
            {
                penalty += 10;
            }

            score += bonus;
            score -= penalty;

            baseScoreFlow = score;
            double flowScore = score - marketStateAdditivePenalty;
            afterAdditiveFlow = flowScore;

            flowScore *= trendScaling;
            afterTrendFlow = flowScore;

            flowScore *= momentumScaling;
            afterMomentumFlow = flowScore;

            flowScore *= comboScaling;
            afterComboFlow = flowScore;

            flowScore *= adxScaling;
            afterAdxFlow = flowScore;

            score = (int)Math.Round(flowScore);
            int finalScoreBeforeCap = score;

            if (ctx.MarketState != null)
            {
                bool noTrend = !ctx.MarketState.IsTrend;
                bool noMomentum = !ctx.MarketState.IsMomentum;

                if (noTrend && noMomentum)
                    score = Math.Min(score, 55);

                if (ctx.MarketState.IsLowVol || !ctx.IsAtrExpanding_M5)
                    score = Math.Min(score, 60);
            }

            string structure =
                breakoutConfirmed ? "BreakoutConfirmed" :
                flagAligned ? "FlagAligned" :
                pullbackAligned ? "PullbackAligned" :
                impulseAligned ? "ImpulseAligned" :
                "None";

            ctx.Log?.Invoke(
                $"[DIR QUALITY] type={request.TypeTag} side={direction} structure={structure} " +
                $"logicBias={logicBias} logicConf={logicConfidence} htfDir={htfDirection} htfConf={htfConfidence:F2} " +
                $"regime={regime} penalty={penalty} marketStatePenalty={marketStateAdditivePenalty} bonus={bonus} finalScore={score}");

            ctx.Log?.Invoke(
                $"[ENTRY SCORE FLOW] type={request.TypeTag} side={direction} " +
                $"baseScore={baseScoreFlow:F1} afterAdditive={afterAdditiveFlow:F1} " +
                $"afterTrendScaling={afterTrendFlow:F1} afterMomentumScaling={afterMomentumFlow:F1} " +
                $"afterCombo={afterComboFlow:F1} afterADX={afterAdxFlow:F1} finalScore={finalScoreBeforeCap} finalCappedScore={score} " +
                $"trendScale={trendScaling:F2} momentumScale={momentumScaling:F2} comboScale={comboScaling:F2} adxScale={adxScaling:F2}");

            return score;
        }

        public static void LogDecision(EntryContext ctx, string typeTag, EntryEvaluation longEval, EntryEvaluation shortEval, TradeDirection selected)
        {
            var eval = longEval ?? shortEval;
            ctx?.Log?.Invoke(
                $"[DIR FLOW] type={typeTag} logicBias={ctx?.LogicBiasDirection ?? TradeDirection.None} evalDir={eval?.Direction ?? TradeDirection.None} score={eval?.Score ?? 0}");
        }

        private static void ResolveHtf(
            EntryContext ctx,
            out InstrumentClass instrumentClass,
            out TradeDirection htfDirection,
            out double htfConfidence)
        {
            instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx.Symbol);
            htfDirection = TradeDirection.None;
            htfConfidence = 0.0;

            switch (instrumentClass)
            {
                case InstrumentClass.FX:
                    htfDirection = ctx.FxHtfAllowedDirection;
                    htfConfidence = ctx.FxHtfConfidence01;
                    break;
                case InstrumentClass.CRYPTO:
                    htfDirection = ctx.CryptoHtfAllowedDirection;
                    htfConfidence = ctx.CryptoHtfConfidence01;
                    break;
                case InstrumentClass.INDEX:
                    htfDirection = ctx.IndexHtfAllowedDirection;
                    htfConfidence = ctx.IndexHtfConfidence01;
                    break;
                case InstrumentClass.METAL:
                    htfDirection = ctx.MetalHtfAllowedDirection;
                    htfConfidence = ctx.MetalHtfConfidence01;
                    break;
            }
        }

        private static bool IsRegimeCompatible(
            EntryContext ctx,
            InstrumentClass instrumentClass,
            TradeDirection direction,
            bool applyTrendRegimePenalty,
            bool breakoutConfirmed,
            bool impulseAligned,
            bool momentumAligned,
            bool structureAligned,
            out string regime)
        {
            bool inTrend = ctx.MarketState?.IsTrend == true || (!ctx.IsRange_M5 && ctx.Adx_M5 >= 18.0);
            bool compressed = ctx.IsRange_M5 || ctx.MarketState?.IsRange == true || ctx.MarketState?.IsLowVol == true;

            if (!applyTrendRegimePenalty)
            {
                regime = compressed ? "SoftRange" : "SoftNeutral";
                return true;
            }

            bool compatible = instrumentClass switch
            {
                InstrumentClass.FX => ctx.Adx_M5 >= 18.0 || breakoutConfirmed || impulseAligned,
                InstrumentClass.INDEX => ctx.Adx_M5 >= 18.0 && momentumAligned && !compressed,
                InstrumentClass.METAL => (inTrend && structureAligned) || breakoutConfirmed,
                InstrumentClass.CRYPTO => momentumAligned && (ctx.IsAtrExpanding_M5 || breakoutConfirmed),
                _ => true
            };

            compatible = compatible && !compressed;

            regime = compatible
                ? "TrendCompatible"
                : $"{instrumentClass}_RegimeMismatch_{direction}";

            return compatible;
        }

        private static bool HasDirectionalStructure(EntryContext ctx, TradeDirection direction) =>
            HasDirectionalBreakout(ctx, direction) ||
            HasDirectionalFlag(ctx, direction) ||
            HasDirectionalPullback(ctx, direction) ||
            HasDirectionalImpulse(ctx, direction);

        private static bool HasDirectionalMomentum(EntryContext ctx, TradeDirection direction)
        {
            if (direction == TradeDirection.None)
                return false;

            if (ctx.BreakoutDirection == direction || ctx.RangeBreakDirection == direction || ctx.ImpulseDirection == direction)
                return true;

            if (ctx.TrendDirection == direction && ctx.LastClosedBarInTrendDirection)
                return true;

            if (ctx.HasBreakout_M1 && ctx.BreakoutDirection == direction)
                return true;

            return false;
        }

        private static bool HasDirectionalBreakout(EntryContext ctx, TradeDirection direction) =>
            direction switch
            {
                TradeDirection.Long => ctx.FlagBreakoutUpConfirmed || ctx.FlagBreakoutUp || ctx.RangeBreakDirection == TradeDirection.Long || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == TradeDirection.Long),
                TradeDirection.Short => ctx.FlagBreakoutDownConfirmed || ctx.FlagBreakoutDown || ctx.RangeBreakDirection == TradeDirection.Short || (ctx.HasBreakout_M1 && ctx.BreakoutDirection == TradeDirection.Short),
                _ => false
            };

        private static bool HasDirectionalFlag(EntryContext ctx, TradeDirection direction) =>
            direction switch
            {
                TradeDirection.Long => ctx.HasFlagLong_M5,
                TradeDirection.Short => ctx.HasFlagShort_M5,
                _ => false
            };

        private static bool HasDirectionalPullback(EntryContext ctx, TradeDirection direction) =>
            direction switch
            {
                TradeDirection.Long => ctx.HasPullbackLong_M5 || ctx.PullbackDepthRLong_M5 > 0.0,
                TradeDirection.Short => ctx.HasPullbackShort_M5 || ctx.PullbackDepthRShort_M5 > 0.0,
                _ => false
            };

        private static bool HasDirectionalImpulse(EntryContext ctx, TradeDirection direction) =>
            direction switch
            {
                TradeDirection.Long => ctx.HasImpulseLong_M5 || ctx.ImpulseDirection == TradeDirection.Long,
                TradeDirection.Short => ctx.HasImpulseShort_M5 || ctx.ImpulseDirection == TradeDirection.Short,
                _ => false
            };

        private static TradeDirection Opposite(TradeDirection direction) =>
            direction switch
            {
                TradeDirection.Long => TradeDirection.Short,
                TradeDirection.Short => TradeDirection.Long,
                _ => TradeDirection.None
            };

        private static EntryStyle ResolveStyle(string typeTag)
        {
            if (string.IsNullOrWhiteSpace(typeTag))
                return EntryStyle.Other;

            string t = typeTag.ToUpperInvariant();
            if (t.Contains("REVERSAL"))
                return EntryStyle.Reversal;
            if (t.Contains("RANGE"))
                return EntryStyle.Range;
            if (t.Contains("FLAG"))
                return EntryStyle.Flag;
            if (t.Contains("PULLBACK"))
                return EntryStyle.Pullback;
            if (t.Contains("BREAKOUT") || t.Contains("IMPULSE") || t.Contains("CONTINUATION"))
                return EntryStyle.Breakout;

            return EntryStyle.Other;
        }

        private enum EntryStyle
        {
            Other,
            Breakout,
            Pullback,
            Flag,
            Reversal,
            Range
        }
    }
}
