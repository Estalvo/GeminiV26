using System;
using cAlgo.API;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.NAS100
{
    /// <summary>
    /// NAS100 SessionGate (UTC)
    ///
    /// Cél:
    /// - Index-specifikus kereskedési időablak
    /// - TradeCore hard gate marad (nem duplázunk máshol)
    ///
    /// Megjegyzés:
    /// - SessionAware döntés az EntryType-okban lesz (metódusokra bontva).
    /// - Itt csak "engedjük-e egyáltalán" jellegű policy (pl. after-hours tilt).
    /// </summary>
    public class NasSessionGate : IGate
    {
        private readonly Robot _bot;

        public NasSessionGate(Robot bot)
        {
            _bot = bot;
        }

        public enum IndexSession
        {
            Asia,
            London,
            Ny,
            AfterHours
        }

        public IndexSession GetSessionUtc()
        {
            // Robot timezone: UTC
            DateTime t = _bot.Server.Time;
            int h = t.Hour;

            // NAS (CFD / index) tipikusan 23/5, de after-hours-ban likviditás rossz.
            // Kezdésnek: legyen trade only London+NY; Asia inkább "adatgyűjtő / low risk".
            // (Ezt később finomítjuk broker/symbol szerint.)
            if (h >= 0 && h < 8) return IndexSession.Asia;
            if (h >= 8 && h < 13) return IndexSession.London;
            if (h >= 13 && h < 22) return IndexSession.Ny;         // 13:00–21:59
            return IndexSession.AfterHours;                        // 22:00–23:59
        }

        public bool AllowEntry(TradeType direction)
        {
            var s = GetSessionUtc();

            // Hard policy (első körben):
            // - AfterHours: tilt (spread + noise)
            // - Asia/London/NY: enged (a részletes viselkedést az EntryType-ok döntik el)
            if (s == IndexSession.AfterHours)
            {
                _bot.Print("[NAS][SESSION] BLOCK AfterHours");
                return false;
            }

            _bot.Print($"[NAS][SESSION] OK {s}");
            return true;
        }
    }
}
