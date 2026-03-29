using cAlgo.API;
using GeminiV26.Interfaces;
using GeminiV26.EntryTypes.METAL;
using System;

namespace GeminiV26.Instruments.XAUUSD
{
    public sealed class XauSessionGate : IGate
    {
        private readonly Robot _bot;
        private readonly XAU_InstrumentProfile _profile;

        public XauSessionGate(Robot bot)
        {
            _bot = bot;
            _profile = XAU_InstrumentMatrix.Get(bot.SymbolName);
        }

        public bool AllowEntry(TradeType direction)
        {
            var t = _bot.Server.Time;
            int h = t.Hour;

            // =========================
            // WEEKEND BLOCK
            // =========================
            if (t.DayOfWeek == DayOfWeek.Saturday || t.DayOfWeek == DayOfWeek.Sunday)
            {
                GlobalLogger.Log("[XAU SESSION] BLOCKED: Weekend");
                return false;
            }

            // =========================
            // ASIAN SESSION (optional)
            // =========================
            if (!_profile.AllowAsian)
            {
                if (h >= 0 && h < 8)
                {
                    GlobalLogger.Log("[XAU SESSION] ALLOWED: XAU_ASIA_ALLOWED");
                    return true;
                }
            }

            // =========================
            // DEFAULT: ALLOW
            // =========================
            GlobalLogger.Log("[XAU SESSION] ALLOWED");
            return true;
        }
    }
}
