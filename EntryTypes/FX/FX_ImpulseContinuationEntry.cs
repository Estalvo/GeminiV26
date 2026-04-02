using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.FX
{
    public class FX_ImpulseContinuationEntry : IEntryType
    {
        public EntryType Type => EntryType.FX_ImpulseContinuation;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            FxDirectionValidation.LogDirectionDebug(ctx);
            if (ctx == null || !ctx.IsReady)
                return Invalid(ctx, TradeDirection.None, "CTX_NOT_READY");

            LogStructure(ctx);

            var structureDirection = ctx.Structure?.StructureDirection ?? TradeDirection.None;
            bool structureDirStrict = structureDirection == TradeDirection.Long || structureDirection == TradeDirection.Short;
            bool structureDirAuthority = ctx.IsStructureDirectionAuthoritative();

            bool hasImpulse = ctx.Structure?.HasImpulse == true;
            bool hasPullback = ctx.Structure?.HasPullback == true;
            bool hasMicroPullback = ctx.Structure?.HasMicroPullback == true;
            bool impulseStrict = hasImpulse;
            bool pullbackStrict = hasPullback;
            bool early = ctx.Structure?.ContinuationEarlySignal == true;
            bool confirmed = ctx.Structure?.ContinuationConfirmedSignal == true;
            bool trendFollowThrough = ctx.LastClosedBarInTrendDirection || ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5;
            int barsSinceImpulse = ctx.GetBarsSinceImpulse(structureDirection);
            bool recentImpulseWidened =
                !hasImpulse &&
                structureDirection != TradeDirection.None &&
                barsSinceImpulse >= 0 &&
                barsSinceImpulse <= 14 &&
                (ctx.Structure?.ImpulseStrength ?? 0.0) >= 0.45;

            if (structureDirection != TradeDirection.Long && structureDirection != TradeDirection.Short)
            {
                var logicDirection = ctx.LogicBiasDirection;
                bool canUseLogicDirection =
                    structureDirAuthority &&
                    (logicDirection == TradeDirection.Long || logicDirection == TradeDirection.Short);
                if (!canUseLogicDirection)
                {
                    ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "STILL_FAIL", "StructureDirection", "NONE",
                        $"reason=NO_DIRECTION source={ctx.Structure?.DirectionSource ?? "NA"} finalValid=false");
                    return Block(ctx, "NO_DIRECTION");
                }

                structureDirection = logicDirection;
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "DIR_OK", "StructureDirection", "LogicBiasDirection",
                    $"source={ctx.Structure?.DirectionSource ?? "NA"} resolvedDir={structureDirection} finalValid=true");
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "ALT_PATH_USED", "StructureDirection", "LogicBiasDirection",
                    $"resolvedDir={structureDirection}");
                barsSinceImpulse = ctx.GetBarsSinceImpulse(structureDirection);
                recentImpulseWidened =
                    !hasImpulse &&
                    barsSinceImpulse >= 0 &&
                    barsSinceImpulse <= 14 &&
                    (ctx.Structure?.ImpulseStrength ?? 0.0) >= 0.45;
                ctx.Log?.Invoke($"[ENTRY][FX_IMPULSE][WIDEN_ALLOW] code=DIRECTION_FROM_LOGIC_BIAS direction={structureDirection}");
            }
            else if (structureDirStrict)
            {
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "DIR_OK", "StructureDirection", "StructureDirection",
                    $"source={ctx.Structure?.DirectionSource ?? "NA"} resolvedDir={structureDirection} strict=true");
            }

            ctx.Log?.Invoke(
                $"[FX][IMPULSE_CHECK] impulse={hasImpulse.ToString().ToLowerInvariant()} micro_pullback={hasMicroPullback.ToString().ToLowerInvariant()} direction={structureDirection} early={early.ToString().ToLowerInvariant()} confirmed={confirmed.ToString().ToLowerInvariant()}");

            bool impulseOk = hasImpulse || recentImpulseWidened || ctx.IsStructureImpulseAuthoritative();
            if (!impulseStrict && impulseOk)
            {
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "IMPULSE_OK", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse} impulseRecent={(ctx.Structure?.ImpulseRecentOk ?? false).ToString().ToLowerInvariant()} finalValid=true");
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "ALT_PATH_USED", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse}");
            }
            if (!impulseOk)
            {
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "STILL_FAIL", "HasImpulse", "NONE",
                    $"barsSinceImpulse={barsSinceImpulse} impulseRecent={(ctx.Structure?.ImpulseRecentOk ?? false).ToString().ToLowerInvariant()}");
                return Block(ctx, "NO_IMPULSE");
            }
            if (recentImpulseWidened)
                ctx.Log?.Invoke($"[ENTRY][FX_IMPULSE][WIDEN_ALLOW] code=RECENT_IMPULSE barsSinceImpulse={barsSinceImpulse}");

            bool shallowPullbackWidened =
                !hasPullback &&
                hasMicroPullback &&
                (early || confirmed) &&
                trendFollowThrough &&
                (ctx.Structure?.PullbackDepth ?? 0.0) <= 0.20;
            bool pullbackOk = hasPullback || shallowPullbackWidened || ctx.IsStructurePullbackAuthoritative();
            if (!pullbackStrict && pullbackOk)
            {
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "PULLBACK_OK", "HasPullback", "PullbackShallowOk|HasMicroPullback",
                    $"depth={(ctx.Structure?.PullbackDepth ?? 0.0):0.00} shallow={(ctx.Structure?.PullbackShallowOk ?? false).ToString().ToLowerInvariant()} finalValid=true");
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "ALT_PATH_USED", "HasPullback", "PullbackShallowOk|HasMicroPullback",
                    $"depth={(ctx.Structure?.PullbackDepth ?? 0.0):0.00}");
            }
            if (!pullbackOk)
            {
                ctx.LogStructureAuthority("FX_ImpulseContinuationEntry", "STILL_FAIL", "HasPullback", "NONE",
                    $"depth={(ctx.Structure?.PullbackDepth ?? 0.0):0.00}");
                return Block(ctx, "NO_PULLBACK");
            }
            if (shallowPullbackWidened)
                ctx.Log?.Invoke($"[ENTRY][FX_IMPULSE][WIDEN_ALLOW] code=SHALLOW_PULLBACK depth={(ctx.Structure?.PullbackDepth ?? 0.0):0.00}");

            bool microPullbackWidened =
                !hasMicroPullback &&
                confirmed &&
                trendFollowThrough;
            if (!hasMicroPullback && !microPullbackWidened)
                return Block(ctx, "NO_MICRO_PULLBACK");
            if (microPullbackWidened)
                ctx.Log?.Invoke("[ENTRY][FX_IMPULSE][WIDEN_ALLOW] code=MICRO_PULLBACK_PROXY");

            if (!early && !confirmed)
                return Block(ctx, "NO_CONTINUATION_SIGNAL");

            string entryType = confirmed ? "CONFIRMED" : "EARLY";
            ctx.Log?.Invoke($"[FX][IMPULSE_ENTRY] type={entryType} direction={structureDirection}");

            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = structureDirection,
                Score = confirmed ? 70 : 60,
                IsValid = true,
                TriggerConfirmed = confirmed,
                Reason = $"FX_IMPULSE_STRUCTURE_{entryType}"
            };

            EntryDirectionQuality.LogDecision(
                ctx,
                Type.ToString(),
                structureDirection == TradeDirection.Long ? eval : null,
                structureDirection == TradeDirection.Short ? eval : null,
                eval.Direction);

            return EntryDecisionPolicy.Normalize(eval);
        }

        private void LogStructure(EntryContext ctx)
        {
            ctx.Log?.Invoke(
                $"[FX][STRUCTURE] impulse={(ctx.Structure?.HasImpulse == true).ToString().ToLowerInvariant()} pullback={(ctx.Structure?.HasPullback == true).ToString().ToLowerInvariant()} micro_pullback={(ctx.Structure?.HasMicroPullback == true).ToString().ToLowerInvariant()} flag={(ctx.Structure?.HasFlag == true).ToString().ToLowerInvariant()} direction={ctx.Structure?.StructureDirection ?? TradeDirection.None}");
        }

        private EntryEvaluation Block(EntryContext ctx, string reason)
        {
            ctx.Log?.Invoke($"[FX][ENTRY_BLOCK] reason={reason}");
            return Invalid(ctx, TradeDirection.None, reason);
        }

        private EntryEvaluation Invalid(EntryContext ctx, TradeDirection dir, string reason)
            => new()
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                Score = 0,
                IsValid = false,
                Reason = reason
            };
    }
}
