using System;
using cAlgo.API;
using GeminiV26.Interfaces;

namespace GeminiV26.Instruments.US30
{
    /// <summary>
    /// US30 Session Gate – Phase 3.7
    /// --------------------------------
    /// Rules:
    /// - Asia: BLOCK
    /// - London: allow only AFTER open +30 min
    /// - Pre-NY: BLOCK
    /// - NY: allow only AFTER open +30 min
    /// - NY close -30 min: BLOCK
    /// </summary>
    public class Us30SessionGate : IGate
    {
        private readonly Robot _bot;

        public Us30SessionGate(Robot bot)
        {
            _bot = bot;
        }

        public bool AllowEntry(TradeType direction)
        {
            DateTime utc = _bot.Server.Time;

            TimeSpan t = utc.TimeOfDay;

            // ===== Session times (UTC) =====
            TimeSpan londonOpen = new TimeSpan(8, 0, 0);
            TimeSpan londonTradeStart = new TimeSpan(8, 30, 0);

            TimeSpan nyOpen = new TimeSpan(14, 30, 0);
            TimeSpan nyTradeStart = new TimeSpan(15, 0, 0);
            TimeSpan nyTradeEnd = new TimeSpan(20, 30, 0);

            // ===== Asia: BLOCK =====
            if (t < londonOpen)
                return false;

            // ===== London =====
            if (t >= londonOpen && t < nyOpen)
            {
                // London open +30 min
                if (t < londonTradeStart)
                    return false;

                // Pre-NY noise block
                return true;
            }

            // ===== NY =====
            if (t >= nyOpen && t <= nyTradeEnd)
            {
                // NY open +30 min
                if (t < nyTradeStart)
                    return false;

                return true;
            }

            // ===== NY close & after =====
            return false;
        }
    }
}
