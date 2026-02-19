using System;
using System.Collections.Generic;
using cAlgo.API;

public class GlobalSessionGate
{
    private readonly Robot _bot;

    public GlobalSessionGate(Robot bot)
    {
        _bot = bot;
    }

    public bool AllowEntry(string symbol)
    {
        // =========================
        // CRYPTO = 24/7, NO SESSION GATE
        // =========================
        if (IsCrypto(symbol))
            return true;

        TimeSpan utc = _bot.Server.Time.TimeOfDay;

        // =================================================
        // 1. GLOBÁLIS FIX TILTÁSOK (EVENT AIRBAG)
        // =================================================

        // NY close / Asia open chaos
        if (IsInRange(utc, new TimeSpan(22, 0, 0), new TimeSpan(0, 30, 0)))
            return false;

        // NY PRE-OPEN positioning
        if (IsInRange(utc, new TimeSpan(13, 50, 0), new TimeSpan(14, 30, 0)))
            return false;

        // NY CASH OPEN airbag
        if (IsInRange(utc, new TimeSpan(14, 30, 0), new TimeSpan(14, 40, 0)))
            return false;

        // NY LUNCH chop (csak FX)
        /*if (IsFx(symbol) &&
            IsInRange(utc, new TimeSpan(16, 30, 0), new TimeSpan(17, 30, 0)))
            return false;
*/
        // =================================================
        // 3. NEW YORK SESSION – AUD/NZD/JPY TILTÁS (LONDON SZABAD)
        // =================================================
        if (IsNySession(utc) && IsFx(symbol))
        {
            // USDJPY kivétel marad NY-ban
            if (symbol == "USDJPY")
                return true;

            if (ContainsAny(symbol, "AUD", "NZD", "JPY"))
                return false;
        }

        return true;
    }

    // =================================================
    // SESSION DEFINÍCIÓK
    // =================================================

    private bool IsAsiaSession(TimeSpan utc)
    {
        // Asia: 00:00 – 07:00 UTC
        return utc >= TimeSpan.Zero && utc < new TimeSpan(7, 0, 0);
    }

    private bool IsLondonSession(TimeSpan utc)
    {
        // London: 08:00 – 16:00 UTC
        return utc >= new TimeSpan(8, 0, 0) && utc < new TimeSpan(16, 0, 0);
    }

    private bool IsNySession(TimeSpan utc)
    {
        // NY: 13:00 – 21:30 UTC
        return utc >= new TimeSpan(13, 0, 0) && utc < new TimeSpan(21, 30, 0);
    }

    // =================================================
    // HELPEREK
    // =================================================

    private bool IsFx(string symbol)
    {
        if (symbol.StartsWith("XAU") || symbol.StartsWith("XAG"))
            return false;

        return symbol.Length == 6 && char.IsLetter(symbol[0]);
    }

    private bool ContainsAny(string symbol, params string[] keys)
    {
        foreach (var k in keys)
            if (symbol.Contains(k))
                return true;

        return false;
    }

    private bool IsInRange(TimeSpan now, TimeSpan from, TimeSpan to)
    {
        if (from < to)
            return now >= from && now < to;

        // over midnight
        return now >= from || now < to;
    }

    private bool IsCrypto(string symbol)
    {
        return symbol.StartsWith("BTC")
            || symbol.StartsWith("ETH")
            || symbol.StartsWith("CRYPTO");
    }
}
