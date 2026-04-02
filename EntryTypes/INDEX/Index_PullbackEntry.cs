using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.EntryTypes;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public class Index_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Pullback;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowPullback)
                return Reject(ctx, TradeDirection.None, 0, "SESSION_MATRIX_ALLOWPULLBACK_DISABLED");

            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, TradeDirection.None, 0, "CTX_NOT_READY");

            var direction = ctx.Structure.StructureDirection;
            bool strictDirection = direction == TradeDirection.Long || direction == TradeDirection.Short;
            bool structureDirAuthority = ctx.IsStructureDirectionAuthoritative();
            bool trendFollowThrough = ctx.LastClosedBarInTrendDirection || ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5;
            if (direction == TradeDirection.None)
            {
                var logicDirection = ctx.LogicBiasDirection;
                bool canUseLogicDirection =
                    structureDirAuthority &&
                    (logicDirection == TradeDirection.Long || logicDirection == TradeDirection.Short) &&
                    (ctx.Structure.PullbackEarlySignal || ctx.Structure.PullbackConfirmedSignal || ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal) &&
                    trendFollowThrough;
                if (!canUseLogicDirection)
                {
                    ctx.LogStructureAuthority("Index_PullbackEntry", "STILL_FAIL", "StructureDirection", "NONE",
                        $"reason=structure_direction_missing source={ctx.Structure.DirectionSource ?? "NA"}");
                    return Reject(ctx, TradeDirection.None, 0, "structure_direction_missing");
                }

                direction = logicDirection;
                if (!strictDirection)
                {
                    ctx.LogStructureAuthority("Index_PullbackEntry", "DIR_OK", "StructureDirection", "LogicBiasDirection",
                        $"source={ctx.Structure.DirectionSource ?? "NA"} resolvedDir={direction}");
                    ctx.LogStructureAuthority("Index_PullbackEntry", "ALT_PATH_USED", "StructureDirection", "LogicBiasDirection",
                        $"resolvedDir={direction}");
                }
                ctx.Log?.Invoke($"[ENTRY][INDEX_PB][WIDEN_ALLOW] code=DIRECTION_FROM_LOGIC_BIAS direction={direction}");
            }
            else if (strictDirection)
            {
                ctx.LogStructureAuthority("Index_PullbackEntry", "DIR_OK", "StructureDirection", "StructureDirection",
                    $"source={ctx.Structure.DirectionSource ?? "NA"} resolvedDir={direction} strict=true");
            }

            int barsSinceImpulse = ctx.GetBarsSinceImpulse(direction);
            bool recentImpulseWidened = !ctx.Structure.HasImpulse && barsSinceImpulse >= 0 && barsSinceImpulse <= 14;
            bool impulseOk = ctx.Structure.HasImpulse || recentImpulseWidened || ctx.IsStructureImpulseAuthoritative();
            if (!ctx.Structure.HasImpulse && impulseOk)
            {
                ctx.LogStructureAuthority("Index_PullbackEntry", "IMPULSE_OK", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse} impulseRecent={ctx.Structure.ImpulseRecentOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("Index_PullbackEntry", "ALT_PATH_USED", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse}");
            }
            if (!impulseOk)
            {
                ctx.LogStructureAuthority("Index_PullbackEntry", "STILL_FAIL", "HasImpulse", "NONE",
                    $"barsSinceImpulse={barsSinceImpulse} impulseRecent={ctx.Structure.ImpulseRecentOk.ToString().ToLowerInvariant()}");
                ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_ERROR] violation=no_trade_without_impulse");
                return Reject(ctx, TradeDirection.None, 0, "no_impulse");
            }
            if (recentImpulseWidened)
                ctx.Log?.Invoke($"[ENTRY][INDEX_PB][WIDEN_ALLOW] code=RECENT_IMPULSE barsSinceImpulse={barsSinceImpulse}");

            bool shallowPullbackWidened =
                !ctx.Structure.HasPullback &&
                ctx.Structure.HasMicroPullback &&
                ctx.Structure.PullbackDepth <= 0.20 &&
                (ctx.Structure.PullbackEarlySignal || ctx.Structure.ContinuationEarlySignal || trendFollowThrough);
            bool pullbackOk = ctx.Structure.HasPullback || shallowPullbackWidened || ctx.IsStructurePullbackAuthoritative();
            if (!ctx.Structure.HasPullback && pullbackOk)
            {
                ctx.LogStructureAuthority("Index_PullbackEntry", "PULLBACK_OK", "HasPullback", "PullbackShallowOk|HasMicroPullback",
                    $"depth={ctx.Structure.PullbackDepth:0.00} shallow={ctx.Structure.PullbackShallowOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("Index_PullbackEntry", "ALT_PATH_USED", "HasPullback", "PullbackShallowOk|HasMicroPullback",
                    $"depth={ctx.Structure.PullbackDepth:0.00}");
            }
            if (!pullbackOk)
            {
                ctx.LogStructureAuthority("Index_PullbackEntry", "STILL_FAIL", "HasPullback", "NONE",
                    $"depth={ctx.Structure.PullbackDepth:0.00}");
                ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_ERROR] violation=no_pullback_entry_without_retrace");
                return Reject(ctx, TradeDirection.None, 0, "no_pullback");
            }
            if (shallowPullbackWidened)
                ctx.Log?.Invoke($"[ENTRY][INDEX_PB][WIDEN_ALLOW] code=SHALLOW_PULLBACK depth={ctx.Structure.PullbackDepth:0.00}");

            bool useEarly = ctx.Structure.PullbackEarlySignal;
            bool useConfirmed = ctx.Structure.PullbackConfirmedSignal;

            if (!useEarly && !useConfirmed)
            {
                ctx.Log?.Invoke("[ENTRY][PULLBACK][WAIT_SIGNAL]");
                return Reject(ctx, TradeDirection.None, 0, "no_signal");
            }

            bool isConfirmedEntry = useConfirmed;

            double timingScore = isConfirmedEntry ? 1.0 : 0.7;
            int score = (int)Math.Round(100.0 * (
                0.4 * ctx.Structure.ImpulseStrength +
                0.4 * (1.0 - ctx.Structure.PullbackDepth) +
                0.2 * timingScore));
            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            ctx.Log?.Invoke("[ENTRY][PULLBACK][STRUCTURE_OK]");
            ctx.Log?.Invoke($"[ENTRY][PULLBACK][SIGNAL] type={(isConfirmedEntry ? "CONFIRMED" : "EARLY")}");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = score,
                IsValid = true,
                Reason = "STRUCTURE_IMPULSE_PULLBACK_OK"
            };
        }

        private static EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, int score, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Pullback,
                Direction = dir,
                Score = Math.Max(0, score),
                IsValid = false,
                Reason = reason
            };
        }
    }
}
