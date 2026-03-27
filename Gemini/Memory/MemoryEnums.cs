namespace Gemini.Memory
{
    public enum MovePhase
    {
        Unknown = 0,
        Impulse = 1,
        Pullback = 2,
        Decay = 3,
        Stale = 4,
        Continuation = 5
    }

    public enum MemoryBuildMode
    {
        Default = 0,
        HistoricalReplay = 1,
        Live = 2
    }

    public enum MemoryTrustLevel
    {
        Unknown = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    public enum ContinuationWindowState
    {
        Unknown = 0,
        Fresh = 1,
        Early = 2,
        Mature = 3,
        Late = 4,
        Exhausted = 5
    }

    public enum MoveExtensionState
    {
        Unknown = 0,
        Normal = 1,
        Extended = 2,
        Overextended = 3
    }
}
