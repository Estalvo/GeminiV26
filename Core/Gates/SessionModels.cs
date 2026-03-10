using System;

public enum SessionBucket
{
    Closed,
    Asia,
    London,
    LondonNyOverlap,
    NewYork,
    NyPreOpen,
    NyCashOpenAirbag,
    NyCloseChaos,
    CryptoAlwaysOn
}

public enum SessionPriority
{
    Blocked = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Best = 4
}

public enum TimeframeTier
{
    Scalping,
    Intraday,
    BroadIntraday
}

public class SessionDecision
{
    public bool Allow { get; set; }
    public string Symbol { get; set; }
    public string Timeframe { get; set; }
    public SessionBucket Bucket { get; set; }
    public SessionPriority Priority { get; set; }
    public bool IsUsDst { get; set; }
    public bool IsEuDst { get; set; }
    public bool IsOverlap { get; set; }
    public string Reason { get; set; }
}
