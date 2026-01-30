using System;

namespace GeminiV26.Data.Models
{
    // POCO – kizárólag log domain
    public class BarLogRecord
    {
        // Mikor írtuk le (rendszeridő)
        public DateTime LogTimestamp { get; set; }

        // A bar nyitási ideje (piaci idő)
        public DateTime BarTimestamp { get; set; }

        public string Symbol { get; set; } = string.Empty;

        // "M1" vagy "M5"
        public string Timeframe { get; set; } = string.Empty;

        // Dedikált OHLC (wickek benne vannak High/Low-ban)
        public double BarOpen { get; set; }
        public double BarHigh { get; set; }
        public double BarLow { get; set; }
        public double BarClose { get; set; }

        public double BarVolume { get; set; }

        // Ha nem elérhető, -1
        public double BarSpread { get; set; }
    }
}
