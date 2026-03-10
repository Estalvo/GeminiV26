using System;

public class SessionCalendar
{
    public bool IsUsDst(DateTime utc)
    {
        int year = utc.Year;
        DateTime start = NthWeekdayOfMonth(year, 3, DayOfWeek.Sunday, 2).Date.AddHours(7); // 02:00 local EST = 07:00 UTC
        DateTime end = NthWeekdayOfMonth(year, 11, DayOfWeek.Sunday, 1).Date.AddHours(6);  // 02:00 local EDT = 06:00 UTC
        return utc >= start && utc < end;
    }

    public bool IsEuDst(DateTime utc)
    {
        int year = utc.Year;
        DateTime start = LastWeekdayOfMonth(year, 3, DayOfWeek.Sunday).Date.AddHours(1);   // 01:00 UTC
        DateTime end = LastWeekdayOfMonth(year, 10, DayOfWeek.Sunday).Date.AddHours(1);    // 01:00 UTC
        return utc >= start && utc < end;
    }

    public TimeSpan GetLondonOpenUtc(DateTime utc) => IsEuDst(utc) ? new TimeSpan(7, 0, 0) : new TimeSpan(8, 0, 0);

    public TimeSpan GetLondonCloseUtc(DateTime utc) => IsEuDst(utc) ? new TimeSpan(15, 0, 0) : new TimeSpan(16, 0, 0);

    public TimeSpan GetNyOpenUtc(DateTime utc) => IsUsDst(utc) ? new TimeSpan(12, 0, 0) : new TimeSpan(13, 0, 0);

    public TimeSpan GetNyCashOpenUtc(DateTime utc) => IsUsDst(utc) ? new TimeSpan(13, 30, 0) : new TimeSpan(14, 30, 0);

    public TimeSpan GetNyCloseUtc(DateTime utc) => IsUsDst(utc) ? new TimeSpan(21, 0, 0) : new TimeSpan(22, 0, 0);

    public bool IsLondonSession(DateTime utc)
    {
        TimeSpan now = utc.TimeOfDay;
        return now >= GetLondonOpenUtc(utc) && now < GetLondonCloseUtc(utc);
    }

    public bool IsNySession(DateTime utc)
    {
        TimeSpan now = utc.TimeOfDay;
        return now >= GetNyOpenUtc(utc) && now < GetNyCloseUtc(utc);
    }

    public bool IsLondonNyOverlap(DateTime utc)
    {
        TimeSpan londonOpen = GetLondonOpenUtc(utc);
        TimeSpan londonClose = GetLondonCloseUtc(utc);
        TimeSpan nyOpen = GetNyOpenUtc(utc);
        TimeSpan nyClose = GetNyCloseUtc(utc);

        TimeSpan overlapStart = londonOpen > nyOpen ? londonOpen : nyOpen;
        TimeSpan overlapEnd = londonClose < nyClose ? londonClose : nyClose;

        if (overlapStart >= overlapEnd)
            return false;

        TimeSpan now = utc.TimeOfDay;
        return now >= overlapStart && now < overlapEnd;
    }

    private static DateTime NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int nth)
    {
        DateTime first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        int delta = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(delta + (nth - 1) * 7);
    }

    private static DateTime LastWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek)
    {
        DateTime firstNextMonth = month == 12
            ? new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : new DateTime(year, month + 1, 1, 0, 0, 0, DateTimeKind.Utc);

        DateTime last = firstNextMonth.AddDays(-1);
        int delta = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return last.AddDays(-delta);
    }
}
