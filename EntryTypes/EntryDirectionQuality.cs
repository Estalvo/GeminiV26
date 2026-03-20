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

            string structure =
                breakoutConfirmed ? "BreakoutConfirmed" :
                flagAligned ? "FlagAligned" :
                pullbackAligned ? "PullbackAligned" :
                impulseAligned ? "ImpulseAligned" :
                "None";

            ctx.Log?.Invoke(
                $"[DIR QUALITY] type={request.TypeTag} side={direction} structure={structure} " +
                $"logicBias={logicBias} logicConf={logicConfidence} htfDir={htfDirection} htfConf={htfConfidence:F2} " +
                $"regime={regime} penalty={penalty} bonus={bonus} finalScore={score}");

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
    }
}
