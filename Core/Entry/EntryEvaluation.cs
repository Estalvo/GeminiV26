namespace GeminiV26.Core.Entry
{
    public class EntryEvaluation
    {
        public string Symbol;
        public EntryType Type;

        // Raw validity from entry evaluator (before downstream filters).
        public bool RawValid;

        // Existing final validity used in routing/decision.
        public bool IsValid;
        public bool FinalValid
        {
            get => IsValid;
            set => IsValid = value;
        }
        public TradeDirection Direction;

        // 0–100
        public int Score;

        // Entry trace diagnostics (observability only).
        public TradeDirection RawDirection;
        public TradeDirection LogicBiasDirection;
        public int RawLogicConfidence;
        public bool PatternDetected;
        public bool FallbackDirectionUsed;
        public string SetupType;
        public int BaseScore;
        public int AfterHtfScoreAdjustment;
        public int AfterPenaltyScore;
        public int FinalScoreSnapshot;
        public int ScoreThresholdSnapshot;
        public TradeDirection DirectionAfterScore;
        public TradeDirection DirectionAfterGates;
        public string LastDirectionDropStage;
        public string LastDirectionDropModule;
        public string EntryTraceClassification;
        public string HtfClassification;

        // Score tracing (observability only).
        public int PreQualityScore;
        public int PostQualityScore;
        public int PostCapScore;
        public bool HasQualityScoreTrace;

        // instrument bias
        public int LogicConfidence;

        public string Reason;
        public string RejectReason;

        public EntryState State = EntryState.NONE;

        public bool TriggerConfirmed;

        public bool HasTrigger
        {
            get => TriggerConfirmed;
            set => TriggerConfirmed = value;
        }

        // Diagnostic-only final acceptance flags (no score impact).
        public bool HasStrongTrigger { get; set; }
        public bool HasStrongStructure { get; set; }

        public bool IsHTFMisaligned;
        public bool IgnoreHTFForDecision;
        public double HtfConfidence01;

        // AUDIT ONLY: HTF trace snapshot captured at source stage.
        public string HtfTraceSourceStage;
        public string HtfTraceSourceModule;
        public string HtfTraceSourceState;
        public TradeDirection HtfTraceSourceAllowedDirection;
        public bool HtfTraceSourceAlign;
        public TradeDirection HtfTraceSourceCandidateDirection;
    }
}
