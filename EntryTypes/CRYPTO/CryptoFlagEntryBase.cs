using System;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;

namespace GeminiV26.EntryTypes.Crypto
{
    public abstract class CryptoFlagEntryBase : IEntryType
    {
        public virtual EntryType Type => EntryType.Crypto_Flag;

        protected abstract string SymbolTag { get; }
        protected abstract int MaxBarsSinceImpulse { get; }
        protected abstract int MaxLateBreakoutBars { get; }
        protected abstract double MinImpulseStrength { get; }
        protected virtual int MaxImpulseMemoryBars => MaxBarsSinceImpulse + 2;
        protected virtual int MaxBreakoutPersistenceBars => MaxLateBreakoutBars + 1;

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);

            if (ctx == null || !ctx.IsReady || ctx.M5 == null || ctx.M5.Count < 20)
                return Reject(ctx, TradeDirection.None, "CTX_NOT_READY");

            TradeDirection dir = ResolveDirection(ctx);
            if (dir == TradeDirection.None)
                return Reject(ctx, TradeDirection.None, "NO_VALID_DIRECTION");

            int barsSinceImpulse = dir == TradeDirection.Long ? ctx.BarsSinceImpulseLong : ctx.BarsSinceImpulseShort;
            if (barsSinceImpulse < 0 && !ctx.IsStructureImpulseAuthoritative())
                return Reject(ctx, dir, "NO_RECENT_IMPULSE");
            bool trendFollowThrough = ctx.LastClosedBarInTrendDirection || ctx.HasReactionCandle_M5 || ctx.M1TriggerInTrendDirection;
            bool recentImpulseWidened =
                !ctx.Structure.HasImpulse &&
                barsSinceImpulse >= 0 &&
                barsSinceImpulse <= MaxImpulseMemoryBars + 2 &&
                (ctx.Structure.ImpulseStrength >= (MinImpulseStrength - 0.05) || ctx.IsAtrExpanding_M5);
            bool impulseOk = ctx.Structure.HasImpulse || recentImpulseWidened || ctx.IsStructureImpulseAuthoritative();
            bool flagShapeWidened =
                !ctx.Structure.HasFlag &&
                ctx.Structure.FlagBars >= 2 &&
                ctx.Structure.FlagCompression <= 0.78 &&
                (ctx.Structure.ContinuationEarlySignal || ctx.Structure.ContinuationConfirmedSignal || trendFollowThrough);
            bool flagOk = ctx.Structure.HasFlag || flagShapeWidened || ctx.IsStructureFlagAuthoritative();
            if (!ctx.Structure.HasImpulse && impulseOk)
            {
                ctx.LogStructureAuthority("CryptoFlagEntryBase", "IMPULSE_OK", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse} impulseRecent={ctx.Structure.ImpulseRecentOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("CryptoFlagEntryBase", "ALT_PATH_USED", "HasImpulse", "ImpulseRecentOk",
                    $"barsSinceImpulse={barsSinceImpulse}");
            }
            if (!ctx.Structure.HasFlag && flagOk)
            {
                ctx.LogStructureAuthority("CryptoFlagEntryBase", "FLAG_OK", "HasFlag", "FlagMessyOk",
                    $"flagBars={ctx.Structure.FlagBars} compression={ctx.Structure.FlagCompression:0.00} messy={ctx.Structure.FlagMessyOk.ToString().ToLowerInvariant()}");
                ctx.LogStructureAuthority("CryptoFlagEntryBase", "ALT_PATH_USED", "HasFlag", "FlagMessyOk",
                    $"flagBars={ctx.Structure.FlagBars}");
            }
            if (!impulseOk || !flagOk)
            {
                ctx.LogStructureAuthority("CryptoFlagEntryBase", "STILL_FAIL", "HasImpulse|HasFlag", "NONE",
                    $"impulseOk={impulseOk.ToString().ToLowerInvariant()} flagOk={flagOk.ToString().ToLowerInvariant()} barsSinceImpulse={barsSinceImpulse}");
                return Reject(ctx, dir, "INVALID_STRUCTURE");
            }
            if (recentImpulseWidened)
                ctx.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][WIDEN_ALLOW] symbol={ctx.Symbol} code=RECENT_IMPULSE barsSinceImpulse={barsSinceImpulse}");
            if (flagShapeWidened)
                ctx.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][WIDEN_ALLOW] symbol={ctx.Symbol} code=MESSY_FLAG flagBars={ctx.Structure.FlagBars} compression={ctx.Structure.FlagCompression:0.00}");

            bool lateWindowWidened =
                barsSinceImpulse > MaxImpulseMemoryBars &&
                barsSinceImpulse <= MaxImpulseMemoryBars + 3 &&
                (ctx.Structure.ContinuationConfirmedSignal || trendFollowThrough) &&
                (dir == TradeDirection.Long ? ctx.TriggerLateScoreLong : ctx.TriggerLateScoreShort) < 70.0;
            if (barsSinceImpulse > MaxImpulseMemoryBars && !lateWindowWidened)
                return Reject(ctx, dir, "LATE_FLAG");
            if (lateWindowWidened)
                ctx.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][WIDEN_ALLOW] symbol={ctx.Symbol} code=LATE_WINDOW barsSinceImpulse={barsSinceImpulse}");

            bool breakoutConfirmed = dir == TradeDirection.Long
                ? (ctx.FlagBreakoutUpConfirmed || ctx.Structure.FlagBreakoutUp)
                : (ctx.FlagBreakoutDownConfirmed || ctx.Structure.FlagBreakoutDown);

            int breakoutBarsSince = dir == TradeDirection.Long ? ctx.BreakoutUpBarsSince : ctx.BreakoutDownBarsSince;
            bool continuationSignal = ctx.Structure.ContinuationConfirmedSignal || ctx.RangeBreakDirection == dir;
            bool persistenceOk =
                ((breakoutConfirmed && breakoutBarsSince <= MaxBreakoutPersistenceBars) ||
                 (continuationSignal && breakoutBarsSince <= MaxLateBreakoutBars)) &&
                trendFollowThrough;

            if (!persistenceOk)
                return Reject(ctx, dir, "LATE_FLAG");

            bool structureValid =
                ctx.Structure.FlagCompression <= 0.62 ||
                ctx.Structure.ContinuationConfirmedSignal ||
                ctx.M1TriggerInTrendDirection ||
                ctx.HasReactionCandle_M5;
            if (!structureValid)
                return Reject(ctx, dir, "INVALID_STRUCTURE");

            bool fakeBreak =
                ctx.RangeFakeoutBars_M1 <= 2 ||
                (!ctx.Structure.ContinuationConfirmedSignal && !ctx.IsAtrExpanding_M5 && !trendFollowThrough) ||
                (ctx.Structure.ImpulseStrength < MinImpulseStrength && !ctx.M1TriggerInTrendDirection);

            if (fakeBreak)
                return Reject(ctx, dir, "FAKE_BREAKOUT");

            int score = 62;
            if (ctx.Structure.ImpulseStrength >= 0.6)
                score += 8;
            if (ctx.Structure.FlagCompression <= 0.40)
                score += 8;
            if (ctx.Structure.ContinuationConfirmedSignal)
                score += 8;
            score = Math.Max(0, Math.Min(100, score));

            var evaluation = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = true,
                Score = score,
                Reason = $"{SymbolTag}_FLAG_STRUCTURE_FIRST_OK"
            };
            ctx.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][RECOGNIZED] symbol={ctx.Symbol} dir={dir} reason={evaluation.Reason} barsSinceImpulse={barsSinceImpulse} breakoutBarsSince={breakoutBarsSince} impulseStrength={ctx.Structure.ImpulseStrength:0.00} compression={ctx.Structure.FlagCompression:0.00}");
            return evaluation;
        }

        private TradeDirection ResolveDirection(EntryContext ctx)
        {
            TradeDirection strictDirection = ctx?.Structure?.StructureDirection ?? TradeDirection.None;
            bool structureDirStrict = strictDirection == TradeDirection.Long || strictDirection == TradeDirection.Short;
            if (structureDirStrict)
            {
                ctx?.LogStructureAuthority("CryptoFlagEntryBase", "DIR_OK", "StructureDirection", "StructureDirection",
                    $"source={ctx?.Structure?.DirectionSource ?? "NA"} resolvedDir={strictDirection} strict=true");
                return strictDirection;
            }

            bool structureDirAuthority = ctx?.IsStructureDirectionAuthoritative() == true;
            TradeDirection preFinalDirection = ctx?.LogicBiasDirection ?? TradeDirection.None;
            TradeDirection routedDirection = ctx?.RoutedDirection ?? TradeDirection.None;
            TradeDirection observedFinalDirection = ctx?.FinalDirection ?? TradeDirection.None;
            TradeDirection resolvedDirection = preFinalDirection != TradeDirection.None
                ? preFinalDirection
                : (routedDirection != TradeDirection.None ? routedDirection : observedFinalDirection);

            if (!structureDirAuthority || resolvedDirection == TradeDirection.None)
            {
                ctx?.LogStructureAuthority("CryptoFlagEntryBase", "STILL_FAIL", "StructureDirection", "NONE",
                    $"reason=NO_DIRECTION source={ctx?.Structure?.DirectionSource ?? "NA"} structureAuthority={structureDirAuthority.ToString().ToLowerInvariant()} finalValid=false");
                ctx?.Log?.Invoke(
                    $"[DIR][CRYPTO][EVAL_NONE] symbol={ctx?.Symbol} entryType={Type} source=LogicBiasDirection logic={preFinalDirection} routed={routedDirection} final={observedFinalDirection}");
                return TradeDirection.None;
            }

            if (observedFinalDirection != TradeDirection.None && observedFinalDirection != resolvedDirection)
            {
                ctx?.Log?.Invoke(
                    $"[DIR][CRYPTO][EVAL_MISMATCH_WARN] symbol={ctx?.Symbol} entryType={Type} source=CompositeDirection chosen={resolvedDirection} logic={preFinalDirection} routed={routedDirection} final={observedFinalDirection}");
            }

            ctx?.LogStructureAuthority("CryptoFlagEntryBase", "DIR_OK", "StructureDirection", "LogicBiasDirection",
                $"source={ctx?.Structure?.DirectionSource ?? "NA"} resolvedDir={resolvedDirection} finalValid=true");
            ctx?.LogStructureAuthority("CryptoFlagEntryBase", "ALT_PATH_USED", "StructureDirection", "LogicBiasDirection",
                $"resolvedDir={resolvedDirection}");
            ctx?.Log?.Invoke(
                $"[DIR][CRYPTO][EVAL_SOURCE] symbol={ctx?.Symbol} entryType={Type} source=CompositeDirection chosen={resolvedDirection} logic={preFinalDirection} routed={routedDirection} final={observedFinalDirection}");
            return resolvedDirection;
        }

        protected EntryEvaluation Reject(EntryContext ctx, TradeDirection dir, string reason)
        {
            string canonicalCode = CanonicalRejectCode(reason);
            ctx?.Log?.Invoke($"[ENTRY][CRYPTO_FLAG][BLOCK][CODE={canonicalCode}] symbol={ctx?.Symbol} dir={dir} detail={reason}");
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = dir,
                IsValid = false,
                Score = 0,
                Reason = canonicalCode,
                RejectReason = canonicalCode
            };
        }

        private static string CanonicalRejectCode(string reason)
        {
            return reason switch
            {
                "CTX_NOT_READY" => "CONTEXT_NOT_READY",
                "NO_VALID_DIRECTION" => "CONTEXT_DIRECTION_NONE",
                "NO_RECENT_IMPULSE" => "ENTRY_NO_IMPULSE",
                "LATE_FLAG" => "ENTRY_TOO_LATE",
                "INVALID_STRUCTURE" => "ENTRY_INVALID_FLAG",
                "FAKE_BREAKOUT" => "TRIGGER_FAKE_BREAKOUT",
                _ => "ENTRY_REJECT_UNSPECIFIED"
            };
        }
    }
}
