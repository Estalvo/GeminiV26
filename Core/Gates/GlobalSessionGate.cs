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

    // ===== Asia FX whitelist =====
    private static readonly HashSet<string> AsiaFxWhitelist = new()
    {
        "USDJPY",
        "EURJPY",
        "GBPJPY",
        "AUDJPY",
        "AUDUSD",
        "NZDUSD",
        "XAUUSD"
    };

    public bool AllowEntry(string symbol)
    {
        // =========================
        // CRYPTO = 24/7, NO SESSION GATE
        // =========================
        if (IsCrypto(symbol))
            return true;

        TimeSpan utc = _bot.Server.Time.TimeOfDay;

        // =================================================
        // 1. GLOBÁLIS FIX TILTÁSOK
        // =================================================

        // NY close / Asia open chaos
        if (IsInRange(utc, new TimeSpan(21, 30, 0), new TimeSpan(1, 30, 0)))
            return false;

        // Asia vége / Közel-Kelet
        if (IsInRange(utc, new TimeSpan(5, 0, 0), new TimeSpan(7, 0, 0)))
            return false;

        // NY open körüli sweep
        if (IsInRange(utc, new TimeSpan(14, 15, 0), new TimeSpan(14, 50, 0)))
            return false;

        // NY lunch / pre-US data chop (FX + Metal only) 14:00–15:50 UTC+1  ==>  13:00–14:50 UTC
        if ((IsFx(symbol) || symbol.StartsWith("XAU") || symbol.StartsWith("XAG"))
            && IsInRange(utc, new TimeSpan(13, 0, 0), new TimeSpan(14, 50, 0)))
            return false;


        // =================================================
        // 2. ASIA SESSION – FX WHITELIST
        // =================================================
        if (IsAsiaSession(utc) && IsFx(symbol))
        {
            if (!AsiaFxWhitelist.Contains(symbol))
                return false;
        }

        // =================================================
        // 3. LONDON + NY – AUD / NZD / JPY TILTÁS
        // =================================================
        if ((IsLondonSession(utc) || IsNySession(utc)) && IsFx(symbol))
        {
            // USDJPY kivétel NY-ban
            if (symbol == "USDJPY" && IsNySession(utc))
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
        // London +1h: 08:00 – 16:00 UTC
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

        return now >= from || now < to;
    }

    private bool IsCrypto(string symbol)
    {
        return symbol.StartsWith("BTC")
            || symbol.StartsWith("ETH")
            || symbol.StartsWith("CRYPTO"); // ha van ilyen prefix
    }

}
