// =========================================================
// GEMINI V26 – TradeRouter (Rulebook 1.0 COMPLIANCE NOTE)
//
// ✅ LEGACY bug megszüntetve:
//    Korábban létezett olyan logika, ami MINDEN nem-null evalt
//    beengedett (IsValid || IsValid == false). EZ MEGSZŰNT.
//
// ✅ Nincs több instrument-specifikus threshold / score gate:
//    A TradeRouter SOHA nem tilt score alapján.
//
// ✅ A router CSAK rangsorol:
//    Kizárólag IsValid == true setupok között választ,
//    a Score kizárólag prioritás, nem belépési engedély.
//
// ✅ Logolás átlátható:
//    jelöltek → valid setupok → nyertes kiválasztás
//
// Ez a fájl a Szabálykönyv 1.0 normatív implementációja.
// =========================================================
using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Core.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GeminiV26.Core
{
    /// <summary>
    /// TradeRouter (Szabálykönyv 1.0)
    /// - CSAK IsValid == true setupokkal dolgozik
    /// - Score csak rangsorol (nem gate)
    /// - Winner = max Score (tie-break: determinisztikus priority)
    /// </summary>
    public class TradeRouter
    {
        private readonly Robot _bot;

        public TradeRouter(Robot bot)
        {
            _bot = bot;
        }

        // =========================================================
        // ENTRY PRIORITY SELECTION (RULEBOOK 1.0)
        // =========================================================
        public EntryEvaluation SelectEntry(List<EntryEvaluation> signals, EntryContext entryContext = null)
        {
            _bot.Print("[TR] SelectEntry CALLED");

            if (signals == null || signals.Count == 0)
                return null;

            int nonNullCount = signals.Count(e => e != null);
            int validCount = signals.Count(e => e != null && e.IsValid);

            _bot.Print($"[TR] evals={signals.Count} nonNull={nonNullCount} valid={validCount} threshold={EntryDecisionPolicy.MinScoreThreshold}");
            LogCandidates("CAND", signals, entryContext);

            EntryEvaluation winner = null;

            foreach (var candidate in signals.Where(e => e != null))
            {
                string decision;
                candidate.FinalValid = candidate.IsValid;
                _bot.Print($"[BASELINE CHECK] type={candidate.Type} score={candidate.Score} valid={candidate.FinalValid.ToString().ToLowerInvariant()} source=ENTRY_ONLY");
                if (candidate.TriggerConfirmed)
                {
                    _bot.Print($"[AUDIT][TRIGGERED] type={candidate.Type} score={candidate.Score} dir={candidate.Direction}");
                }

                if (!candidate.FinalValid)
                {
                    decision = "REJECT";
                    _bot.Print(TradeLogIdentity.WithTempId($"[BLOCK] type={candidate.Type} dir={candidate.Direction} score={candidate.Score} reason=invalid_candidate", entryContext));
                }
                else if (!ApplyFxAcceptanceFilters(candidate, entryContext))
                {
                    decision = "REJECT";
                }
                else
                {
                    decision = candidate.TriggerConfirmed ? "ACCEPT" : "ACCEPT_SCORE_MODEL";

                    if (!candidate.TriggerConfirmed)
                    {
                        _bot.Print(TradeLogIdentity.WithTempId(
                            $"[EXEC BLOCK] NOT TRIGGERED → BLOCK execution | {candidate.Type} score={candidate.Score}",
                            entryContext));
                    }

                    if (decision == "ACCEPT_SCORE_MODEL")
                    {
                        _bot.Print(TradeLogIdentity.WithTempId(
                            "[EXEC BLOCK] SCORE_MODEL cannot execute (no trigger)",
                            entryContext));
                    }

                    _bot.Print(
                        $"[AUDIT][EXEC CHECK] type={candidate.Type} " +
                        $"trigger={candidate.TriggerConfirmed} " +
                        $"valid={candidate.IsValid} " +
                        $"state={candidate.State}");

                    if (!IsExecutable(candidate))
                    {
                        _bot.Print(TradeLogIdentity.WithTempId(
                            $"[EXEC FILTER] candidate not executable | trigger={candidate.TriggerConfirmed.ToString().ToLowerInvariant()} valid={candidate.IsValid.ToString().ToLowerInvariant()}",
                            entryContext));
                        continue;
                    }

                    if (winner == null
                        || candidate.Score > winner.Score
                        || (candidate.Score == winner.Score
                            && GetTypePriority(_bot.SymbolName, candidate.Type) < GetTypePriority(_bot.SymbolName, winner.Type)))
                    {
                        winner = candidate;
                    }
                }

                bool structureAligned = IsStructureAligned(entryContext, candidate.Direction);
                bool hasImpulse = entryContext?.HasImpulse_M5 == true;
                TradeDirection routedHtfAllowedDirection = candidate.HtfTraceSourceAllowedDirection;
                string routedHtfState = candidate.HtfTraceSourceState ?? "N/A";
                bool routedHtfAlign = candidate.HtfTraceSourceAlign;
                bool htfAligned = routedHtfAlign;
                TradeDirection consumerAllowedDirection = routedHtfAllowedDirection;
                string consumerHtfState = routedHtfState;
                string assetClass = SymbolRouting.ResolveInstrumentClass(candidate.Symbol ?? _bot.SymbolName).ToString();
                bool continuationValid = !IsContinuationSetup(candidate.Type)
                    || (entryContext?.MarketState?.IsTrend == true && candidate.Direction == entryContext.TrendDirection);
                bool pullbackValid = candidate.Direction == TradeDirection.Long
                    ? entryContext?.HasPullbackLong_M5 == true
                    : candidate.Direction == TradeDirection.Short && entryContext?.HasPullbackShort_M5 == true;
                bool breakoutValid =
                    entryContext?.BreakoutDirection == candidate.Direction ||
                    entryContext?.RangeBreakDirection == candidate.Direction;

                _bot.Print(
                    $"[AUDIT][VALIDITY] type={candidate.Type} " +
                    $"structure={structureAligned} " +
                    $"impulse={hasImpulse} " +
                    $"htfAlign={htfAligned} " +
                    $"continuation={continuationValid} " +
                    $"pullback={pullbackValid} " +
                    $"breakout={breakoutValid}");
                _bot.Print(
                    $"[AUDIT][HTF FLOW][ROUTER_CONSUME] symbol={candidate.Symbol ?? _bot.SymbolName} asset={assetClass} entryType={candidate.Type} " +
                    $"stage={nameof(TradeRouter)} module={nameof(TradeRouter)} htfState={routedHtfState} allowedDirection={routedHtfAllowedDirection} " +
                    $"align={routedHtfAlign} candidateDirection={candidate.Direction}");
                bool hasSourceSnapshot =
                    !string.IsNullOrWhiteSpace(candidate.HtfTraceSourceStage)
                    || !string.IsNullOrWhiteSpace(candidate.HtfTraceSourceModule)
                    || !string.IsNullOrWhiteSpace(candidate.HtfTraceSourceState);
                bool divergence = hasSourceSnapshot
                    && (!string.Equals(candidate.HtfTraceSourceState, routedHtfState, StringComparison.Ordinal)
                        || candidate.HtfTraceSourceAllowedDirection != routedHtfAllowedDirection
                        || candidate.HtfTraceSourceAlign != routedHtfAlign);
                _bot.Print(
                    $"[AUDIT][HTF ROUTER] asset={assetClass} symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} " +
                    $"routerHtfState={routedHtfState} routerAllowedDirection={routedHtfAllowedDirection} routerAlign={routedHtfAlign} " +
                    $"sourceHtfState={candidate.HtfTraceSourceState ?? "N/A"} sourceAllowedDirection={candidate.HtfTraceSourceAllowedDirection} " +
                    $"sourceAlign={candidate.HtfTraceSourceAlign} divergence={divergence}");
                if (assetClass == nameof(InstrumentClass.CRYPTO))
                {
                    _bot.Print(
                        $"[AUDIT][HTF ROUTER][CRYPTO] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} candidateDirection={candidate.Direction} " +
                        $"sourceHtfState={candidate.HtfTraceSourceState ?? "N/A"} sourceAllowedDirection={candidate.HtfTraceSourceAllowedDirection} sourceAlign={candidate.HtfTraceSourceAlign} " +
                        $"routedHtfState={routedHtfState} routedAllowedDirection={routedHtfAllowedDirection} routedAlign={routedHtfAlign} divergence={divergence}");
                }
                if (!hasSourceSnapshot)
                {
                    _bot.Print(
                        $"[AUDIT][HTF CONFLICT][SKIPPED_NO_SOURCE] symbol={candidate.Symbol ?? _bot.SymbolName} asset={assetClass} entryType={candidate.Type} " +
                        $"candidateDirection={candidate.Direction} routedState={routedHtfState} routedAllowedDirection={routedHtfAllowedDirection} routedAlign={routedHtfAlign}");
                }
                else if (divergence)
                {
                    _bot.Print(
                        $"[AUDIT][HTF CONFLICT][GLOBAL] symbol={candidate.Symbol ?? _bot.SymbolName} asset={assetClass} entryType={candidate.Type} " +
                        $"stageA={candidate.HtfTraceSourceStage ?? "SOURCE"} stageB={nameof(TradeRouter)} stateA={candidate.HtfTraceSourceState ?? "N/A"} stateB={routedHtfState} " +
                        $"allowedDirA={candidate.HtfTraceSourceAllowedDirection} allowedDirB={routedHtfAllowedDirection} " +
                        $"htfAlignA={candidate.HtfTraceSourceAlign} htfAlignB={routedHtfAlign}");
                    if (assetClass == nameof(InstrumentClass.CRYPTO))
                    {
                        _bot.Print(
                            $"[AUDIT][HTF CONFLICT][CRYPTO] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} candidateDirection={candidate.Direction} " +
                            $"sourceHtfState={candidate.HtfTraceSourceState ?? "N/A"} sourceAllowedDirection={candidate.HtfTraceSourceAllowedDirection} sourceAlign={candidate.HtfTraceSourceAlign} " +
                            $"routedHtfState={routedHtfState} routedAllowedDirection={routedHtfAllowedDirection} routedAlign={routedHtfAlign}");
                    }
                }
                if (candidate.Type == EntryType.Index_Flag && candidate.TriggerConfirmed)
                {
                    _bot.Print(
                        $"[AUDIT][HTF TRACE][CONSUMER] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} " +
                        $"candidateDirection={candidate.Direction} consumedHtfState={consumerHtfState} " +
                        $"consumedAllowedDirection={consumerAllowedDirection} consumedHtfAlign={htfAligned} module={nameof(TradeRouter)}");

                    if (!string.Equals(candidate.HtfTraceSourceState ?? "N/A", consumerHtfState ?? "N/A", StringComparison.Ordinal)
                        || candidate.HtfTraceSourceAllowedDirection != consumerAllowedDirection
                        || candidate.HtfTraceSourceAlign != htfAligned
                        || !IsDirectionInterpretationMatch(candidate.Direction, candidate.HtfTraceSourceAllowedDirection, consumerAllowedDirection))
                    {
                        _bot.Print(
                            $"[AUDIT][HTF CONFLICT] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} " +
                            $"stageA={candidate.HtfTraceSourceStage ?? "UnknownSource"} stageB={nameof(TradeRouter)} " +
                            $"stateA={candidate.HtfTraceSourceState ?? "N/A"} stateB={consumerHtfState} " +
                            $"allowedDirA={candidate.HtfTraceSourceAllowedDirection} allowedDirB={consumerAllowedDirection} " +
                            $"htfAlignA={candidate.HtfTraceSourceAlign} htfAlignB={htfAligned}");
                    }
                }
                if (candidate.Type == EntryType.Index_Flag && candidate.TriggerConfirmed)
                {
                    _bot.Print(
                        $"[AUDIT][HTF SOURCE] type={candidate.Type} " +
                        $"htfAlign={htfAligned} " +
                        $"biasState={routedHtfState} " +
                        $"allowedDir={routedHtfAllowedDirection} " +
                        $"candidateDir={candidate.Direction}");
                }

                if (!candidate.IsValid)
                {
                    _bot.Print(
                        $"[AUDIT][DEATH] type={candidate.Type} " +
                        $"score={candidate.Score} " +
                        $"reason={candidate.Reason} " +
                        $"trigger={candidate.TriggerConfirmed} " +
                        $"state={candidate.State}");

                    int nearMissThreshold = Math.Max(60, EntryDecisionPolicy.MinScoreThreshold - 5);
                    if (candidate.TriggerConfirmed && candidate.Score >= nearMissThreshold)
                    {
                        _bot.Print(
                            $"[AUDIT][NEAR MISS] type={candidate.Type} " +
                            $"score={candidate.Score} reason={candidate.Reason}");
                    }
                }

                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[SCORE][DECISION_INPUT] entry={candidate.Score} logic={entryContext?.LogicBiasConfidence ?? 0} final={PositionContext.ComputeFinalConfidenceValue(candidate.Score, entryContext?.LogicBiasConfidence ?? 0)} threshold={EntryDecisionPolicy.MinScoreThreshold}",
                    entryContext));

                candidate.RejectReason = ResolveRejectReason(candidate, EntryDecisionPolicy.MinScoreThreshold);
                if (candidate.Type == EntryType.Index_Flag
                    && candidate.TriggerConfirmed
                    && candidate.Reason != null
                    && candidate.Reason.Contains("HTF_MISMATCH"))
                {
                    bool hasDirection = candidate.Direction != TradeDirection.None;
                    bool hasHtf = routedHtfAllowedDirection != TradeDirection.None;
                    bool trueDirectionMismatch = IsTrueDirectionMismatch(candidate.Direction, routedHtfAllowedDirection);
                    string htfClassification = ResolveHtfRejectClassification(routedHtfAlign, trueDirectionMismatch, !hasDirection || !hasHtf);

                    _bot.Print(
                        $"[AUDIT][HTF TRACE][REJECT] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} " +
                        $"candidateDirection={candidate.Direction} rejectReason={candidate.Reason} " +
                        $"currentHtfState={routedHtfState} currentAllowedDirection={routedHtfAllowedDirection} " +
                        $"currentHtfAlign={routedHtfAlign} module={nameof(TradeRouter)}");

                    _bot.Print(
                        $"[AUDIT][HTF ROUTER] type={candidate.Type} " +
                        $"reason={htfClassification} " +
                        $"biasState={routedHtfState} " +
                        $"allowedDir={routedHtfAllowedDirection} " +
                        $"candidateDir={candidate.Direction}");
                }
                string rejectText = $"{candidate.Reason} {candidate.RejectReason}";
                if (!string.IsNullOrWhiteSpace(rejectText) && rejectText.Contains("HTF_MISMATCH"))
                {
                    bool hasDirection = candidate.Direction != TradeDirection.None;
                    bool hasHtf = routedHtfAllowedDirection != TradeDirection.None;
                    bool trueDirectionMismatch = IsTrueDirectionMismatch(candidate.Direction, routedHtfAllowedDirection);
                    string htfClassification = ResolveHtfRejectClassification(routedHtfAlign, trueDirectionMismatch, !hasDirection || !hasHtf);
                    _bot.Print(
                        $"[AUDIT][HTF REJECT ANALYSIS] symbol={candidate.Symbol ?? _bot.SymbolName} asset={assetClass} entryType={candidate.Type} " +
                        $"candidateDirection={candidate.Direction} htfAllowedDirection={routedHtfAllowedDirection} htfState={routedHtfState} " +
                        $"align={routedHtfAlign} trueDirectionMismatch={(trueDirectionMismatch ? "YES" : "NO")} " +
                        $"classification={htfClassification} rejectModule={nameof(TradeRouter)}");
                }
                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[ENTRY DECISION] symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} side={candidate.Direction} " +
                    $"rawValid={candidate.RawValid.ToString().ToLowerInvariant()} finalValid={candidate.FinalValid.ToString().ToLowerInvariant()} " +
                    $"preScore={(candidate.HasQualityScoreTrace ? candidate.PreQualityScore : candidate.Score)} " +
                    $"postScore={(candidate.HasQualityScoreTrace ? candidate.PostQualityScore : candidate.Score)} " +
                    $"cappedScore={(candidate.HasQualityScoreTrace ? candidate.PostCapScore : candidate.Score)} " +
                    $"threshold={EntryDecisionPolicy.MinScoreThreshold} reason={candidate.RejectReason} " +
                    $"state={candidate.State} trigger={candidate.TriggerConfirmed.ToString().ToLowerInvariant()} → {decision}",
                    entryContext));

                if (candidate.RejectReason != "ACCEPTED")
                {
                    if (candidate.Type == EntryType.Index_Flag
                        && candidate.TriggerConfirmed
                        && candidate.Reason != null
                        && candidate.Reason.Contains("HTF_MISMATCH"))
                    {
                        _bot.Print(
                            $"[AUDIT][HTF CONFLICT] type={candidate.Type} " +
                            $"htfAlign={htfAligned} " +
                            $"reason={candidate.Reason}");
                    }
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[ENTRY REJECT DETAIL] symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} side={candidate.Direction} " +
                        $"reason={candidate.RejectReason} structureAligned={IsStructureAligned(entryContext, candidate.Direction).ToString().ToLowerInvariant()} " +
                        $"momentumAligned={IsMomentumAligned(entryContext, candidate.Direction).ToString().ToLowerInvariant()} " +
                        $"trend={(entryContext?.TrendDirection ?? TradeDirection.None)} adx={(entryContext?.Adx_M5 ?? 0.0):F1}",
                        entryContext));
                }
            }

            if (winner == null)
            {
                _bot.Print("[TR] NO CANDIDATE PASSED GLOBAL ENTRY DECISION");
                return null;
            }

            _bot.Print(TradeLogIdentity.WithTempId($"[ACCEPT] type={winner.Type} dir={winner.Direction} score={winner.Score} reason={winner.Reason}", entryContext));
            _bot.Print(TradeLogIdentity.WithTempId($"[TR] WINNER: {winner.Type} dir={winner.Direction} score={winner.Score} valid={winner.IsValid} reason={winner.Reason}", entryContext));
            return winner;
        }

        private static bool IsExecutable(EntryEvaluation c)
        {
            return c != null
                && c.IsValid
                && c.TriggerConfirmed;
        }

        private static bool IsDirectionInterpretationMatch(
            TradeDirection candidateDirection,
            TradeDirection sourceAllowedDirection,
            TradeDirection consumerAllowedDirection)
        {
            bool sourceBlocks = sourceAllowedDirection != TradeDirection.None && sourceAllowedDirection != candidateDirection;
            bool consumerBlocks = consumerAllowedDirection != TradeDirection.None && consumerAllowedDirection != candidateDirection;
            return sourceBlocks == consumerBlocks;
        }

        private static bool IsTrueDirectionMismatch(TradeDirection candidateDirection, TradeDirection allowedDirection)
        {
            return candidateDirection != TradeDirection.None
                && allowedDirection != TradeDirection.None
                && candidateDirection != allowedDirection;
        }

        private static string ResolveHtfRejectClassification(bool align, bool trueDirectionMismatch, bool noDirectionCase)
        {
            if (noDirectionCase)
                return "HTF_NO_DIRECTION";

            if (trueDirectionMismatch)
                return "HTF_MISMATCH";

            if (!align)
                return "HTF_NOT_ALIGNED";

            return "HTF_OK";
        }

        private static string ResolveRejectReason(EntryEvaluation candidate, int threshold)
        {
            if (candidate == null)
                return "INVALID_STRUCTURE";

            if (!candidate.RawValid)
                return "INVALID_STRUCTURE";

            if (candidate.Score < threshold)
                return "BELOW_THRESHOLD";

            if (candidate.Score >= threshold && !candidate.FinalValid)
                return "INVALID_POST_EVAL";

            return "ACCEPTED";
        }

        private static bool IsStructureAligned(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null || direction == TradeDirection.None)
                return false;

            return ctx.BreakoutDirection == direction
                || ctx.RangeBreakDirection == direction
                || ctx.ImpulseDirection == direction
                || (direction == TradeDirection.Long ? ctx.HasFlagLong_M5 || ctx.HasPullbackLong_M5 : ctx.HasFlagShort_M5 || ctx.HasPullbackShort_M5);
        }

        private static bool IsMomentumAligned(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null || direction == TradeDirection.None)
                return false;

            return ctx.BreakoutDirection == direction
                || ctx.RangeBreakDirection == direction
                || ctx.ImpulseDirection == direction
                || (ctx.TrendDirection == direction && ctx.LastClosedBarInTrendDirection);
        }


        private bool ApplyFxAcceptanceFilters(EntryEvaluation eval, EntryContext entryContext)
        {
            const int HtfMismatchPenalty = 10;
            const int TimingEarlyScoreBoost = 2;
            const int TimingLateScorePenalty = 2;
            const int TimingEarlyThresholdRelax = 1;
            const int TimingLateThresholdTighten = 1;
            bool continuationAuthority =
                entryContext?.MarketState?.IsTrend == true &&
                eval?.Direction == entryContext.TrendDirection &&
                entryContext.HasImpulse_M5 &&
                entryContext.IsAtrExpanding_M5;

            if (eval == null || !eval.IsValid)
                return false;

            string symbol = eval.Symbol ?? entryContext?.Symbol ?? _bot.SymbolName;
            var assetClass = SymbolRouting.ResolveInstrumentClass(symbol);
            if (assetClass != InstrumentClass.FX)
                return true;

            eval.IgnoreHTFForDecision = false;
            if (string.IsNullOrWhiteSpace(eval.HtfTraceSourceStage))
            {
                _bot.Print(
                    $"[AUDIT][HTF CONFLICT][SKIPPED_NO_SOURCE] symbol={symbol} asset={assetClass} entryType={eval.Type} candidateDirection={eval.Direction}");
            }

            eval.HtfConfidence01 = Math.Max(0.0, Math.Min(1.0, eval.HtfConfidence01));
            eval.IsHTFMisaligned = eval.HtfTraceSourceAllowedDirection != TradeDirection.None
                && eval.Direction != TradeDirection.None
                && eval.Direction != eval.HtfTraceSourceAllowedDirection;

            int scoreBeforeTimingBias = eval.Score;
            int thresholdBias = 0;

            if (entryContext != null && IsContinuationSetup(eval.Type))
            {
                bool isLong = eval.Direction == TradeDirection.Long;
                bool hasEarlyContinuation = isLong ? entryContext.HasEarlyContinuationLong : entryContext.HasEarlyContinuationShort;
                bool hasLateContinuation = isLong ? entryContext.HasLateContinuationLong : entryContext.HasLateContinuationShort;

                if (hasEarlyContinuation && !hasLateContinuation)
                {
                    eval.Score = Math.Max(0, eval.Score + TimingEarlyScoreBoost);
                    thresholdBias = -TimingEarlyThresholdRelax;
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[TIMING][EARLY_BIAS] symbol={symbol} type={eval.Type} score={scoreBeforeTimingBias}->{eval.Score}", entryContext));
                }
                else if (hasLateContinuation && !hasEarlyContinuation)
                {
                    eval.Score = Math.Max(0, eval.Score - TimingLateScorePenalty);
                    thresholdBias = TimingLateThresholdTighten;
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[TIMING][LATE_BIAS] symbol={symbol} type={eval.Type} score={scoreBeforeTimingBias}->{eval.Score}", entryContext));
                }
            }

            int decisionScore = eval.Score;
            int effectiveMinThreshold = EntryDecisionPolicy.MinScoreThreshold + thresholdBias;
            if (eval.IsHTFMisaligned)
            {
                if (eval.HtfConfidence01 >= 0.80 && entryContext?.LogicBiasConfidence < 60)
                {
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[HTF][BLOCK] strong opposite HTF + weak LTF type={eval.Type} dir={eval.Direction} " +
                        $"score={eval.Score} htfConf={eval.HtfConfidence01:F2} logicConf={entryContext?.LogicBiasConfidence ?? 0}", entryContext));
                    if (!continuationAuthority)
                        return RejectFxCandidate(eval, decisionScore, "HTF_STRONG_OPPOSITE_LTF_WEAK", entryContext);

                    eval.Score -= 8;
                }

                int originalScore = eval.Score;
                eval.Score = Math.Max(0, eval.Score - HtfMismatchPenalty);
                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[HTF][PENALTY] mismatch applied type={eval.Type} dir={eval.Direction} " +
                    $"score={originalScore}->{eval.Score} htfConf={eval.HtfConfidence01:F2} logicConf={entryContext?.LogicBiasConfidence ?? 0}", entryContext));
            }

            if (decisionScore < effectiveMinThreshold)
                return RejectFxCandidate(eval, decisionScore, "FX_SCORE_BELOW_THRESHOLD", entryContext);

            if (!eval.HasTrigger)
            {
                if (eval.State == EntryState.SETUP_DETECTED)
                    return RejectFxCandidate(eval, decisionScore, "FX_EARLY_BLOCK", entryContext);

                if (continuationAuthority)
                {
                    _bot.Print(TradeLogIdentity.WithTempId(
                        "[AUTH BLOCK] continuation authority cannot bypass trigger",
                        entryContext));
                    return RejectFxCandidate(eval, decisionScore, "AUTH_TRIGGER_BYPASS_BLOCK", entryContext);
                }

                return RejectFxCandidate(eval, decisionScore, "FX_TRIGGER_REQUIRED", entryContext);
            }

            if (decisionScore < 45)
                return RejectFxCandidate(eval, decisionScore, "FX_MIN_QUALITY_BLOCK", entryContext);

            return true;
        }

        private static bool IsContinuationSetup(EntryType type)
        {
            switch (type)
            {
                case EntryType.XAU_Pullback:
                case EntryType.XAU_Flag:
                case EntryType.FX_Pullback:
                case EntryType.FX_Flag:
                case EntryType.FX_RangeBreakout:
                case EntryType.FX_FlagContinuation:
                case EntryType.FX_MicroContinuation:
                case EntryType.FX_MicroStructure:
                case EntryType.FX_ImpulseContinuation:
                case EntryType.Index_Breakout:
                case EntryType.Index_Pullback:
                case EntryType.Index_Flag:
                case EntryType.Crypto_Flag:
                case EntryType.Crypto_Pullback:
                case EntryType.Crypto_RangeBreakout:
                    return true;
                default:
                    return false;
            }
        }

        private bool RejectFxCandidate(EntryEvaluation eval, int decisionScore, string reasonToken, EntryContext entryContext)
        {
            eval.IsValid = false;
            eval.Reason = string.IsNullOrWhiteSpace(eval.Reason)
                ? $"[{reasonToken}]"
                : $"{eval.Reason} [{reasonToken}]";

            _bot.Print(TradeLogIdentity.WithTempId(
                $"[FX FILTER] type={eval.Type} dir={eval.Direction} score={eval.Score} decisionScore={decisionScore} reason={reasonToken}", entryContext));
            _bot.Print(TradeLogIdentity.WithTempId(
                $"[BLOCK] type={eval.Type} dir={eval.Direction} score={eval.Score} reason={reasonToken}", entryContext));

            return false;
        }

        // =========================================================
        // LOGGING
        // =========================================================
        private void LogCandidates(string scope, IEnumerable<EntryEvaluation> list, EntryContext entryContext)
        {
            if (list == null) return;

            foreach (var e in list)
            {
                if (e == null) continue;
                _bot.Print(TradeLogIdentity.WithTempId($"[CANDIDATE] type={e.Type} dir={e.Direction} valid={e.IsValid.ToString().ToLowerInvariant()} score={e.Score} reason={e.Reason}", entryContext));
                _bot.Print(TradeLogIdentity.WithTempId($"[TR] {scope} {e.Type} dir={e.Direction} valid={e.IsValid} state={e.State} trigger={e.TriggerConfirmed} score={e.Score} reason={e.Reason}", entryContext));
            }
        }

        // Deterministic tie-break (only used when scores are equal)
        private int GetTypePriority(string symbol, EntryType type)
        {
            string sym = SymbolRouting.NormalizeSymbol(symbol);
            var instrumentClass = SymbolRouting.ResolveInstrumentClass(sym);

            // =========================
            // XAU
            // =========================
            if (instrumentClass == InstrumentClass.METAL)
            {
                switch (type)
                {
                    case EntryType.XAU_Flag: return 0;   // ⭐ trendforrás
                    case EntryType.XAU_Pullback: return 1;
                    case EntryType.XAU_Impulse: return 2;
                    case EntryType.XAU_Reversal: return 3;
                    default: return 100;
                }
            }

            // =========================
            // INDEX
            // =========================
            if (instrumentClass == InstrumentClass.INDEX)
            {
                switch (type)
                {
                    case EntryType.Index_Flag: return 0; // ⭐ legjobb, strukturált
                    case EntryType.Index_Pullback: return 1; // continuation
                    case EntryType.Index_Breakout: return 2; // csak ha más nincs
                }
            }

            // =========================
            // CRYPTO
            // =========================
            if (instrumentClass == InstrumentClass.CRYPTO)
            {
                switch (type)
                {
                    case EntryType.Crypto_Flag: return 0;
                    case EntryType.Crypto_Pullback: return 1;
                    case EntryType.Crypto_RangeBreakout: return 2;
                    case EntryType.Crypto_Impulse: return 3;
                    default: return 100;
                }
            }

            // =========================
            // FX (fallback)
            // =========================
            switch (type)
            {
                case EntryType.FX_Flag: return 0;

                case EntryType.FX_FlagContinuation: return 1;

                case EntryType.FX_MicroStructure: return 2;

                case EntryType.FX_MicroContinuation: return 3;

                case EntryType.FX_ImpulseContinuation: return 4;

                case EntryType.FX_Pullback: return 5;

                case EntryType.FX_RangeBreakout: return 6;

                case EntryType.FX_Reversal: return 7;

                default: return 100;
            }
        }
    }
}
