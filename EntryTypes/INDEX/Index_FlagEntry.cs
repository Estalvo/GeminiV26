using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Matrix;
using GeminiV26.EntryTypes;
using System;

namespace GeminiV26.EntryTypes.INDEX
{
    public sealed class Index_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.Index_Flag;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            var matrix = ctx?.SessionMatrixConfig ?? SessionMatrixDefaults.Neutral;
            if (!matrix.AllowFlag)
                return Reject(ctx, "SESSION_MATRIX_ALLOWFLAG_DISABLED", 0, TradeDirection.None);

            if (ctx == null || !ctx.IsReady)
                return Reject(ctx, "CTX_NOT_READY", 0, TradeDirection.None);

            var direction = ctx.Structure.StructureDirection;
            bool structureDirStrict = direction == TradeDirection.Long || direction == TradeDirection.Short;
            bool structureDirAuthority = ctx.IsStructureDirectionAuthoritative();
            bool trendFollowThrough = ctx.LastClosedBarInTrendDirection || ctx.M1TriggerInTrendDirection || ctx.HasReactionCandle_M5;
            if (direction == TradeDirection.None)
            {
                var logicDirection = ctx.LogicBiasDirection;
                bool canUseLogicDirection =
                    structureDirAuthority &&
                    (logicDirection == TradeDirection.Long || logicDirection == TradeDirection.Short);
                if (!canUseLogicDirection)
                {
                    ctx.LogStructureAuthority("Index_FlagEntry", "STILL_FAIL", "StructureDirection", "NONE",
                        $"reason=NO_DIRECTION source={ctx.Structure.DirectionSource ?? "NA"} finalValid=false");
                    ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK][CODE=NO_DIRECTION]");
                    return Reject(ctx, "NO_DIRECTION", 0, TradeDirection.None);
                }

                direction = logicDirection;
                ctx.LogStructureAuthority("Index_FlagEntry", "DIR_OK", "StructureDirection", "LogicBiasDirection",
                    $"source={ctx.Structure.DirectionSource ?? "NA"} resolvedDir={direction} finalValid=true");
                ctx.LogStructureAuthority("Index_FlagEntry", "ALT_PATH_USED", "StructureDirection", "LogicBiasDirection",
                    $"resolvedDir={direction}");
                ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][WIDEN_ALLOW] code=DIRECTION_FROM_LOGIC_BIAS direction={direction}");
            }
            else if (structureDirStrict)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "DIR_OK", "StructureDirection", "StructureDirection",
                    $"source={ctx.Structure.DirectionSource ?? "NA"} resolvedDir={direction} strict=true");
            }

            int barsSinceImpulse = ctx.GetBarsSinceImpulse(direction);
            bool recentDirectionalImpulse = barsSinceImpulse >= 0 && barsSinceImpulse <= 14;
            bool hasImpulse = ctx.Structure.HasImpulse || recentDirectionalImpulse || ctx.IsStructureImpulseAuthoritative();
            if (!ctx.Structure.HasImpulse && hasImpulse)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "IMPULSE_OK", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse} impulseRecent={ctx.Structure.ImpulseRecentOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("Index_FlagEntry", "ALT_PATH_USED", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse}");
            }
            if (!hasImpulse)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "STILL_FAIL", "HasImpulse", "NONE",
                    $"barsSinceImpulse={barsSinceImpulse}");
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK] reason=NO_IMPULSE");
                return Reject(ctx, "NO_IMPULSE", 0, TradeDirection.None);
            }

            bool shallowPullbackWidened =
                !ctx.Structure.HasPullback &&
                ctx.Structure.HasMicroPullback &&
                ctx.Structure.PullbackDepth <= 0.22 &&
                ctx.Structure.ImpulseStrength >= 0.45 &&
                (ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal || trendFollowThrough);
            bool pullbackOk = ctx.Structure.HasPullback || shallowPullbackWidened || ctx.IsStructurePullbackAuthoritative();
            if (!ctx.Structure.HasPullback && pullbackOk)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "PULLBACK_OK", "HasPullback", "PullbackShallowOk|HasMicroPullback",
                    $"depth={ctx.Structure.PullbackDepth:0.00} shallow={ctx.Structure.PullbackShallowOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("Index_FlagEntry", "ALT_PATH_USED", "HasPullback", "PullbackShallowOk|HasMicroPullback",
                    $"depth={ctx.Structure.PullbackDepth:0.00}");
            }
            if (!pullbackOk)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "STILL_FAIL", "HasPullback", "NONE",
                    $"depth={ctx.Structure.PullbackDepth:0.00}");
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK][CODE=NO_PULLBACK]");
                return Reject(ctx, "NO_PULLBACK", 0, TradeDirection.None);
            }
            if (shallowPullbackWidened)
                ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][WIDEN_ALLOW] code=SHALLOW_PULLBACK depth={ctx.Structure.PullbackDepth:0.00}");

            bool hasFlag = ctx.Structure.HasFlag || ctx.IsStructureFlagAuthoritative();
            bool weakButUsableFlag =
                ctx.Structure.FlagBars >= 2 &&
                ctx.Structure.PullbackDepth <= 0.72 &&
                ctx.Structure.FlagCompression <= 0.80;
            if (!ctx.Structure.HasFlag && hasFlag)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "FLAG_OK", "HasFlag", "FlagMessyOk",
                    $"flagBars={ctx.Structure.FlagBars} compression={ctx.Structure.FlagCompression:0.00} messy={ctx.Structure.FlagMessyOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("Index_FlagEntry", "ALT_PATH_USED", "HasFlag", "FlagMessyOk",
                    $"flagBars={ctx.Structure.FlagBars}");
            }
            if (!hasFlag && !weakButUsableFlag)
            {
                ctx.LogStructureAuthority("Index_FlagEntry", "STILL_FAIL", "HasFlag", "NONE",
                    $"flagBars={ctx.Structure.FlagBars} compression={ctx.Structure.FlagCompression:0.00}");
                ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][BLOCK][CODE=INVALID_FLAG] flagBars={ctx.Structure.FlagBars} pullbackDepth={ctx.Structure.PullbackDepth:0.00} compression={ctx.Structure.FlagCompression:0.00}");
                return Reject(ctx, "INVALID_FLAG", 0, TradeDirection.None);
            }

            bool validBreakout =
                (direction == TradeDirection.Long && ctx.Structure.FlagBreakoutUp) ||
                (direction == TradeDirection.Short && ctx.Structure.FlagBreakoutDown);
            bool hasContinuationSignal = ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal;

            bool momentumOk =
                ctx.IsAtrExpanding_M5 ||
                ctx.Adx_M5 >= 17.5 ||
                hasContinuationSignal ||
                validBreakout;
            if (!momentumOk)
            {
                ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][BLOCK] reason=NO_MOMENTUM adx={ctx.Adx_M5:0.0} atrExp={ctx.IsAtrExpanding_M5.ToString().ToLowerInvariant()}");
                return Reject(ctx, "NO_MOMENTUM", 0, TradeDirection.None);
            }

            if (!validBreakout && !hasContinuationSignal)
            {
                ctx.Log?.Invoke("[ENTRY][INDEX_FLAG][BLOCK] reason=INVALID_FLAG");
                return Reject(ctx, "INVALID_FLAG", 0, TradeDirection.None);
            }

            double score01 =
                0.5 * ctx.Structure.ImpulseStrength +
                0.3 * (1.0 - ctx.Structure.PullbackDepth) +
                0.2 * (1.0 - ctx.Structure.FlagCompression);

            int score = (int)Math.Round(100.0 * score01);
            if (recentDirectionalImpulse && !ctx.Structure.HasImpulse)
                score -= 8;
            if (!validBreakout && hasContinuationSignal)
                score -= 6;
            if (!ctx.IsAtrExpanding_M5 && ctx.Adx_M5 < 20.0)
                score -= 5;

            score += (int)Math.Round(matrix.EntryScoreModifier);
            score = Math.Max(0, Math.Min(100, score));

            ctx.Log?.Invoke($"[ENTRY][INDEX_FLAG][RECOGNIZED] direction={direction} score={score} breakout={validBreakout.ToString().ToLowerInvariant()} continuation={hasContinuationSignal.ToString().ToLowerInvariant()}");

            return new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = direction,
                Score = score,
                IsValid = true,
                TriggerConfirmed = validBreakout,
                Reason = "STRUCTURE_IMPULSE_PULLBACK_FLAG_OK"
            };
        }

        private static EntryEvaluation Reject(EntryContext ctx, string reason, int score, TradeDirection dir)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = EntryType.Index_Flag,
                Direction = dir,
                IsValid = false,
                Score = Math.Max(0, score),
                Reason = reason
            };
        }
    }
}
