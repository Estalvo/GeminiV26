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
    // Csak ezek maradhatnak aktívak Asia alatt
    private static readonly HashSet<string> AsiaFxWhitelist = new()
    {
        "USDJPY",
        "EURJPY",
        "GBPJPY",
        "AUDJPY",
        "AUDUSD",
        "NZDUSD"
    };

    public bool AllowEntry(string symbol)
    {
        TimeSpan utc = _bot.Server.Time.TimeOfDay;

        // =================================================
        // 1. GLOBÁLIS FIX TILTÁSOK (minden instrumentum)
        // =================================================

        // NY close / Asia open chaos
        if (IsInRange(utc, new TimeSpan(21, 30, 0), new TimeSpan(1, 30, 0)))
            return false;

        // Asia vége / Közel-Kelet
        if (IsInRange(utc, new TimeSpan(5, 0, 0), new TimeSpan(7, 0, 0)))
            return false;

        // NY open körüli sweep / fake move
        if (IsInRange(utc, new TimeSpan(14, 15, 0), new TimeSpan(14, 50, 0)))
            return false;

        // =================================================
        // 2. ASIA SESSION – FX SZŰRÉS
        // =================================================
        if (IsAsiaSession(utc) && IsFx(symbol))
        {
            if (!AsiaFxWhitelist.Contains(symbol))
                return false;
        }

        return true;
    }

    // =================================================
    // HELPEREK
    // =================================================

    private bool IsAsiaSession(TimeSpan utc)
    {
        // Asia: 00:00 – 07:00 UTC
        return utc >= TimeSpan.Zero && utc < new TimeSpan(7, 0, 0);
    }

    private bool IsFx(string symbol)
    {
        // Metalok kizárása
        if (symbol.StartsWith("XAU") || symbol.StartsWith("XAG"))
            return false;

        // klasszikus FX: 6 betűs devizapár
        return symbol.Length == 6 && char.IsLetter(symbol[0]);
    }

    private bool IsInRange(TimeSpan now, TimeSpan from, TimeSpan to)
    {
        // normál intervallum
        if (from < to)
            return now >= from && now < to;

        // wrap intervallum (pl. 21:30–01:30)
        return now >= from || now < to;
    }
}
