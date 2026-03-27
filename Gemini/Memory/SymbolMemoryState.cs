namespace Gemini.Memory
{
    public sealed class SymbolMemoryState
    {
        public string Symbol { get; set; } = string.Empty;
        public MovePhase MovePhase { get; set; } = MovePhase.Unknown;
        public int MoveAgeBars { get; set; }
        public int PullbackCount { get; set; }
        public int BarsSinceImpulse { get; set; }
        public bool IsStaleImpulse { get; set; }
        public bool IsImpulseDecay { get; set; }
        public bool HasActiveImpulse { get; set; }
        public int ImpulseDirection { get; set; }
        public double LastImpulseHigh { get; set; }
        public double LastImpulseLow { get; set; }
        public ContinuationWindowState ContinuationWindowState { get; set; } = ContinuationWindowState.Unknown;
        public MoveExtensionState MoveExtensionState { get; set; } = MoveExtensionState.Unknown;
        public int ContinuationAttemptCount { get; set; }
        public int BarsSinceBreak { get; set; } = -1;
        public int BarsSinceFirstPullback { get; set; } = -1;
        public double DistanceFromFastStructureAtr { get; set; }
        public double ImpulseFreshnessScore { get; set; }
        public double ContinuationFreshnessScore { get; set; }
        public double TriggerLateScore { get; set; }
        public string SessionName { get; set; } = string.Empty;
        public double SessionFatigueScore { get; set; }
        public MemoryTrustLevel TrustLevel { get; set; } = MemoryTrustLevel.Unknown;
        public MemoryBuildMode BuildMode { get; set; } = MemoryBuildMode.Default;
        public bool IsBuilt { get; set; }
        public bool IsResolved { get; set; }
        public bool IsUsable { get; set; }
        public string ResolveFailureReason { get; set; } = string.Empty;
    }
}
