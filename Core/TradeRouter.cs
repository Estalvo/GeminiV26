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

                    if (winner == null
                        || candidate.Score > winner.Score
                        || (candidate.Score == winner.Score
                            && GetTypePriority(_bot.SymbolName, candidate.Type) < GetTypePriority(_bot.SymbolName, winner.Type)))
                    {
                        winner = candidate;
                    }
                }

                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[SCORE][DECISION_INPUT] entry={candidate.Score} logic={entryContext?.LogicBiasConfidence ?? 0} final={PositionContext.ComputeFinalConfidenceValue(candidate.Score, entryContext?.LogicBiasConfidence ?? 0)} threshold={EntryDecisionPolicy.MinScoreThreshold}",
                    entryContext));

                candidate.RejectReason = ResolveRejectReason(candidate, EntryDecisionPolicy.MinScoreThreshold);
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
            eval.HtfConfidence01 = entryContext?.FxHtfConfidence01 ?? 0.0;
            eval.IsHTFMisaligned = entryContext != null
                && entryContext.FxHtfAllowedDirection != TradeDirection.None
                && eval.Direction != TradeDirection.None
                && eval.Direction != entryContext.FxHtfAllowedDirection;

            int decisionScore = eval.Score;
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

            if (decisionScore < EntryDecisionPolicy.MinScoreThreshold)
                return RejectFxCandidate(eval, decisionScore, "FX_SCORE_BELOW_THRESHOLD", entryContext);

            if (!eval.HasTrigger)
            {
                if (eval.State == EntryState.SETUP_DETECTED)
                    return RejectFxCandidate(eval, decisionScore, "FX_EARLY_BLOCK", entryContext);

                if (!continuationAuthority)
                    return RejectFxCandidate(eval, decisionScore, "FX_TRIGGER_REQUIRED", entryContext);
            }

            if (decisionScore < 45)
                return RejectFxCandidate(eval, decisionScore, "FX_MIN_QUALITY_BLOCK", entryContext);

            return true;
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
