using System.Collections.Generic;
using GeminiV26.EntryTypes;
using GeminiV26.Core;

namespace GeminiV26.Core.Entry
{
    /// <summary>
    /// EntryRouter
    /// Feladata:
    /// - instrumentenként összegyűjti az EntryType-ok javaslatait
    /// - NEM dönt, NEM priorizál, NEM nyit trade-et
    /// </summary>
    public class EntryRouter
    {
        private readonly List<IEntryType> _entryTypes;

        public EntryRouter(IEnumerable<IEntryType> entryTypes)
        {
            _entryTypes = new List<IEntryType>(entryTypes);
        }

        /// <summary>
        /// Instrumentenként kiértékeli az összes EntryType-ot,
        /// és visszaadja az EntryEvaluation-öket.
        /// </summary>
        public Dictionary<string, List<EntryEvaluation>> Evaluate(
            IEnumerable<EntryContext> contexts)
        {
            var result = new Dictionary<string, List<EntryEvaluation>>();

            foreach (var ctx in contexts)
            {
                if (!result.TryGetValue(ctx.Symbol, out var evalList))
                {
                    evalList = new List<EntryEvaluation>();
                    result[ctx.Symbol] = evalList;
                }

                foreach (var entryType in _entryTypes)
                {
                    TradeDirection htfAllowedDirection = ctx?.ResolveAssetHtfAllowedDirection() ?? TradeDirection.None;
                    var startClassification = HtfClassificationModel.ComputeHtfClassification(
                        TradeDirection.None,
                        htfAllowedDirection);
                    ctx?.Print(
                        $"[ENTRY_TRACE][START] symbol={ctx?.Symbol} entryType={entryType?.GetType().Name} stage=START candidateDirection={TradeDirection.None} score=NA classification={startClassification}");

                    var eval = entryType.Evaluate(ctx);

                    if (eval != null)
                    {
                        eval.RawDirection = eval.Direction;
                        eval.LogicBiasDirection = ctx?.LogicBiasDirection ?? TradeDirection.None;
                        if (eval.RawLogicConfidence <= 0)
                            eval.RawLogicConfidence = ctx?.LogicBiasConfidence ?? 0;

                        if (!eval.PatternDetected)
                            eval.PatternDetected = CryptoDirectionFallback.DetectPattern(ctx, eval.RawDirection != TradeDirection.None ? eval.RawDirection : eval.LogicBiasDirection);

                        if (eval.RawDirection == TradeDirection.None && eval.LogicBiasDirection != TradeDirection.None)
                        {
                            CryptoDirectionFallback.ApplyIfEligible(ctx, eval, eval.Reason);
                            eval.RawDirection = eval.Direction;
                        }

                        eval.SetupType = eval.Type.ToString();
                        eval.BaseScore = eval.Score;
                        eval.AfterHtfScoreAdjustment = eval.Score;
                        eval.AfterPenaltyScore = eval.Score;
                        eval.FinalScoreSnapshot = eval.Score;
                        eval.ScoreThresholdSnapshot = EntryDecisionPolicy.MinScoreThreshold;
                        eval.DirectionAfterScore = eval.Direction;
                        eval.DirectionAfterGates = eval.Direction;
                        eval.EntryTraceClassification = "ENTRY_UNKNOWN";
                        HtfClassificationModel.InitializeEntryHtfClassification(
                            eval,
                            eval.Direction,
                            htfAllowedDirection);

                        ctx?.Print(
                            $"[ENTRY_TRACE][LOGIC] symbol={ctx?.Symbol} entryType={eval.Type} stage=LOGIC candidateDirection={eval.Direction} score={eval.Score} classification={eval.HtfClassification} " +
                            $"rawDirection={eval.RawDirection} logicBiasDirection={eval.LogicBiasDirection} logicConfidence={eval.RawLogicConfidence} " +
                            $"patternDetected={eval.PatternDetected.ToString().ToLowerInvariant()} setupType={eval.SetupType}");

                        ctx?.Print(
                            $"[ENTRY_TRACE][DIRECTION_SOURCE] symbol={ctx?.Symbol} entryType={eval.Type} biasDirection={eval.LogicBiasDirection} rawDirection={eval.RawDirection} " +
                            $"fallbackUsed={eval.FallbackDirectionUsed.ToString().ToLowerInvariant()} patternDetected={eval.PatternDetected.ToString().ToLowerInvariant()}");

                        eval = EntryDecisionPolicy.Normalize(eval);
                        eval.FinalScoreSnapshot = eval.Score;
                        eval.DirectionAfterScore = eval.Direction;
                        bool passedThreshold = eval.Score >= EntryDecisionPolicy.MinScoreThreshold;
                        ctx?.Print(
                            $"[ENTRY_TRACE][SCORE] symbol={ctx?.Symbol} entryType={eval.Type} stage=SCORE candidateDirection={eval.Direction} score={eval.Score} classification={eval.HtfClassification} " +
                            $"baseScore={eval.BaseScore} afterHtfScoreAdjustment={eval.AfterHtfScoreAdjustment} afterPenalty={eval.AfterPenaltyScore} finalScore={eval.FinalScoreSnapshot} " +
                            $"scoreThreshold={EntryDecisionPolicy.MinScoreThreshold} passedThreshold={passedThreshold.ToString().ToLowerInvariant()}");

                        eval.Reason = "[ROUTER] " + (eval.Reason ?? "");
                        ctx?.Print(
                            $"[ENTRY_TRACE][FINAL] symbol={ctx?.Symbol} entryType={eval.Type} stage=ENTRY_ROUTER candidateDirection={eval.Direction} score={eval.Score} " +
                            $"classification={eval.HtfClassification} finalCandidateDirection={eval.Direction} finalScore={eval.Score} blocked={(!eval.IsValid).ToString().ToLowerInvariant()} finalReason={eval.Reason ?? "NA"}");
                    }

                    // DEBUG – marad
                    System.Diagnostics.Debug.WriteLine(
                        $"[DEBUG_ROUTER] {ctx.Symbol} {entryType.GetType().Name} " +
                        $"{(eval == null ? "eval=NULL" : $"score={eval.Score} valid={eval.IsValid} dir={eval.Direction} reason={eval.Reason ?? ""}")}"
                    );

                    if (eval == null)
                        continue;

                    // Instrument-keveredés kizárása
                    if (eval.Symbol != ctx.Symbol)
                        continue;

                    // ❗ FONTOS:
                    // Router NEM gate-el Direction / IsValid alapján
                    // Ezek executor szintű döntések

                    evalList.Add(eval);
                }
            }

            return result;
        }
    }
}
