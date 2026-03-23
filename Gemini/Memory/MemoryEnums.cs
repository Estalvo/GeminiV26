namespace Gemini.Memory
{
    public enum MovePhase
    {
        Unknown = 0,
        Impulse = 1,
        Pullback = 2,
        Decay = 3,
        Stale = 4
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
}
