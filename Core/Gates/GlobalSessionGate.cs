using System;
using cAlgo.API;
using GeminiV26.Core;

public class GlobalSessionGate
{
    private readonly Robot _bot;
    private readonly SessionCalendar _calendar;
    private int _blockedCount;
    private int _allowedCount;

    public GlobalSessionGate(Robot bot)
    {
        _bot = bot;
        _calendar = new SessionCalendar();
    }

    public bool AllowEntry(string symbol)
    {
        return AllowEntry(symbol, _bot.TimeFrame);
    }

    public bool AllowEntry(string symbol, TimeFrame tf)
    {
        return GetDecision(symbol, tf).Allow;
    }

    public bool IsBlockedSession(DateTime utcNow)
    {
        var time = utcNow.TimeOfDay;

        // Block: 19:30 → 23:59
        if (time >= TimeSpan.FromHours(19.5))
            return true;

        // Block: 00:00 → 02:00
        if (time < TimeSpan.FromHours(2))
            return true;

        return false;
    }

    public void RecordHardBlock(bool blocked)
    {
        if (blocked)
            _blockedCount++;
        else
            _allowedCount++;
    }

    public int BlockedCount => _blockedCount;
    public int AllowedCount => _allowedCount;

    public SessionDecision GetDecision(string symbol, TimeFrame tf)
    {
        DateTime utc = _bot.Server.Time;
        bool usDst = _calendar.IsUsDst(utc);
        bool euDst = _calendar.IsEuDst(utc);
        TimeSpan nyOpen = _calendar.GetNyOpenUtc(utc);
        TimeSpan nyCashOpen = _calendar.GetNyCashOpenUtc(utc);
        TimeSpan nyClose = _calendar.GetNyCloseUtc(utc);
        TimeSpan londonOpen = _calendar.GetLondonOpenUtc(utc);
        TimeSpan londonClose = _calendar.GetLondonCloseUtc(utc);
        bool isOverlap = _calendar.IsLondonNyOverlap(utc);

        GlobalLogger.Log(_bot, string.Format("[SESSION_CALENDAR] utc={0:o} usDst={1} euDst={2} nyOpen={3} nyCashOpen={4} nyClose={5} londonOpen={6} londonClose={7}",
            utc, usDst, euDst, nyOpen, nyCashOpen, nyClose, londonOpen, londonClose));

        var decision = new SessionDecision
        {
            Symbol = symbol,
            Timeframe = FormatTimeFrame(tf),
            IsUsDst = usDst,
            IsEuDst = euDst,
            IsOverlap = isOverlap,
            Bucket = SessionBucket.Closed,
            Priority = SessionPriority.Blocked,
            Allow = false,
            Reason = "Default blocked"
        };

        if (utc.DayOfWeek == DayOfWeek.Monday &&
            utc.TimeOfDay >= new TimeSpan(7, 0, 0) &&
            utc.TimeOfDay < new TimeSpan(8, 30, 0))
        {
            decision.Reason = "MONDAY_0700_0830_BLOCK";
            LogGate(symbol, tf, GetTimeframeTier(tf), decision);
            return decision;
        }

        if (IsCrypto(symbol))
        {
            decision.Bucket = SessionBucket.CryptoAlwaysOn;
            decision.Priority = SessionPriority.Medium;
            decision.Allow = true;
            decision.Reason = "Crypto always on";
            LogGate(symbol, tf, GetTimeframeTier(tf), decision);
            return decision;
        }

        TimeframeTier tier = GetTimeframeTier(tf);
        SessionBucket bucket = ResolveBucket(utc, nyCashOpen, nyClose);
        decision.Bucket = bucket;

        if (bucket == SessionBucket.NyPreOpen)
        {
            decision.Reason = "NY pre-open airbag";
            LogGate(symbol, tf, tier, decision);
            return decision;
        }

        if (bucket == SessionBucket.NyCashOpenAirbag)
        {
            decision.Reason = "NY cash-open airbag";
            LogGate(symbol, tf, tier, decision);
            return decision;
        }

        if (bucket == SessionBucket.NyCloseChaos)
        {
            decision.Reason = "NY close chaos airbag";
            LogGate(symbol, tf, tier, decision);
            return decision;
        }

        bool isFx = IsFx(symbol);
        bool isIndex = IsIndex(symbol);
        bool isMetal = IsMetal(symbol);

        ApplyTimeframePolicy(tier, bucket, decision);

        if (!decision.Allow)
        {
            LogGate(symbol, tf, tier, decision);
            return decision;
        }

        ApplyInstrumentPriority(symbol, isFx, isIndex, isMetal, bucket, decision);

        if (bucket == SessionBucket.NewYork && isFx)
        {
            if (string.Equals(symbol, "USDJPY", StringComparison.OrdinalIgnoreCase))
            {
                decision.Reason = "USDJPY exception in NY session";
            }
            else if (ContainsAny(symbol, "AUD", "NZD", "JPY"))
            {
                decision.Allow = false;
                decision.Priority = SessionPriority.Blocked;
                decision.Reason = "NY FX filter: AUD/NZD/JPY blocked";
                GlobalLogger.Log(_bot, string.Format("[SESSION_FILTER] symbol={0} rule=NY_FX_BLOCK allow=false", symbol));
            }
        }

        if (string.IsNullOrEmpty(decision.Reason))
            decision.Reason = "Session allowed";

        LogGate(symbol, tf, tier, decision);
        return decision;
    }

    private SessionBucket ResolveBucket(DateTime utc, TimeSpan nyCashOpen, TimeSpan nyClose)
    {
        TimeSpan now = utc.TimeOfDay;
        TimeSpan nyPreOpenStart = nyCashOpen - TimeSpan.FromMinutes(40);
        TimeSpan nyCashAirbagEnd = nyCashOpen + TimeSpan.FromMinutes(10);
        TimeSpan nyCloseChaosStart = nyClose - TimeSpan.FromMinutes(30);
        TimeSpan nyCloseChaosEnd = nyClose + TimeSpan.FromMinutes(30);

        if (IsInRange(now, nyPreOpenStart, nyCashOpen))
            return SessionBucket.NyPreOpen;

        if (IsInRange(now, nyCashOpen, nyCashAirbagEnd))
            return SessionBucket.NyCashOpenAirbag;

        if (IsInRange(now, nyCloseChaosStart, nyCloseChaosEnd))
            return SessionBucket.NyCloseChaos;

        if (_calendar.IsLondonNyOverlap(utc))
            return SessionBucket.LondonNyOverlap;

        if (_calendar.IsLondonSession(utc))
            return SessionBucket.London;

        if (_calendar.IsNySession(utc))
            return SessionBucket.NewYork;

        if (now >= TimeSpan.Zero && now < _calendar.GetLondonOpenUtc(utc))
            return SessionBucket.Asia;

        return SessionBucket.Closed;
    }

    private static void ApplyTimeframePolicy(TimeframeTier tier, SessionBucket bucket, SessionDecision decision)
    {
        switch (tier)
        {
            case TimeframeTier.Scalping:
                if (bucket == SessionBucket.London || bucket == SessionBucket.LondonNyOverlap || bucket == SessionBucket.NewYork)
                    decision.Allow = true;
                decision.Reason = decision.Allow ? "Scalping session allowed" : "Scalping blocks this session";
                break;

            case TimeframeTier.Intraday:
                if (bucket == SessionBucket.London || bucket == SessionBucket.LondonNyOverlap || bucket == SessionBucket.NewYork)
                    decision.Allow = true;
                decision.Reason = decision.Allow ? "Intraday session allowed" : "Intraday blocks this session";
                break;

            case TimeframeTier.BroadIntraday:
                if (bucket == SessionBucket.London || bucket == SessionBucket.LondonNyOverlap || bucket == SessionBucket.NewYork || bucket == SessionBucket.Asia)
                    decision.Allow = true;
                decision.Reason = decision.Allow ? "Broad intraday session allowed" : "Broad intraday blocks this session";
                if (bucket == SessionBucket.Asia && decision.Allow)
                    decision.Priority = SessionPriority.Low;
                break;
        }
    }

    private static void ApplyInstrumentPriority(string symbol, bool isFx, bool isIndex, bool isMetal, SessionBucket bucket, SessionDecision decision)
    {
        if (isFx)
        {
            if (bucket == SessionBucket.LondonNyOverlap)
                decision.Priority = SessionPriority.Best;
            else if (bucket == SessionBucket.London)
                decision.Priority = SessionPriority.High;
            else if (bucket == SessionBucket.NewYork)
                decision.Priority = SessionPriority.Medium;
            else if (bucket == SessionBucket.Asia && decision.Allow)
                decision.Priority = SessionPriority.Low;
            return;
        }

        if (isIndex)
        {
            if (bucket == SessionBucket.NewYork)
                decision.Priority = SessionPriority.High;
            else if (bucket == SessionBucket.LondonNyOverlap)
                decision.Priority = SessionPriority.Medium;
            else if (bucket == SessionBucket.London)
                decision.Priority = SessionPriority.Medium;
            else if (bucket == SessionBucket.Asia && decision.Allow)
                decision.Priority = SessionPriority.Low;
            return;
        }

        if (isMetal)
        {
            if (bucket == SessionBucket.LondonNyOverlap)
                decision.Priority = SessionPriority.Best;
            else if (bucket == SessionBucket.London || bucket == SessionBucket.NewYork)
                decision.Priority = SessionPriority.High;
            else if (bucket == SessionBucket.Asia && decision.Allow)
                decision.Priority = SessionPriority.Low;
            return;
        }

        if (decision.Allow && decision.Priority == SessionPriority.Blocked)
            decision.Priority = SessionPriority.Medium;
    }

    public static TimeframeTier GetTimeframeTier(TimeFrame tf)
    {
        string tfName = tf.ToString();

        if (tf == TimeFrame.Minute || tfName == "Minute2" || tfName == "Minute3")
            return TimeframeTier.Scalping;

        if (tf == TimeFrame.Minute5)
            return TimeframeTier.Intraday;

        if (tf == TimeFrame.Minute15 || tf == TimeFrame.Minute30 || tf == TimeFrame.Hour)
            return TimeframeTier.BroadIntraday;

        return TimeframeTier.Intraday;
    }

    private static string FormatTimeFrame(TimeFrame tf)
    {
        string tfName = tf.ToString();
        if (tf == TimeFrame.Minute) return "M1";
        if (tfName == "Minute2") return "M2";
        if (tfName == "Minute3") return "M3";
        if (tf == TimeFrame.Minute5) return "M5";
        if (tf == TimeFrame.Minute15) return "M15";
        if (tf == TimeFrame.Minute30) return "M30";
        if (tf == TimeFrame.Hour) return "H1";
        return tfName;
    }

    private void LogGate(string symbol, TimeFrame tf, TimeframeTier tier, SessionDecision decision)
    {
        GlobalLogger.Log(_bot, string.Format("[SESSION_GATE] symbol={0} tf={1} tier={2} bucket={3} priority={4} allow={5} reason={6}",
            symbol,
            FormatTimeFrame(tf),
            tier,
            decision.Bucket,
            decision.Priority,
            decision.Allow,
            decision.Reason));
    }

    private static bool IsFx(string symbol)
    {
        return SymbolRouting.ResolveInstrumentClass(symbol) == InstrumentClass.FX;
    }

    private static bool IsMetal(string symbol)
    {
        return SymbolRouting.ResolveInstrumentClass(symbol) == InstrumentClass.METAL;
    }

    private static bool IsIndex(string symbol)
    {
        var canonical = SymbolRouting.NormalizeSymbol(symbol);
        return SymbolRouting.ResolveInstrumentClass(canonical) == InstrumentClass.INDEX
            || canonical == "SPX500";
    }

    private static bool ContainsAny(string symbol, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (symbol.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }

    private static bool IsInRange(TimeSpan now, TimeSpan from, TimeSpan to)
    {
        if (from < to)
            return now >= from && now < to;

        return now >= from || now < to;
    }

    private static bool IsCrypto(string symbol)
    {
        return SymbolRouting.ResolveInstrumentClass(symbol) == InstrumentClass.CRYPTO;
    }
}
