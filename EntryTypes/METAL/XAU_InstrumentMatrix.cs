using System;
using System.Collections.Generic;

namespace GeminiV26.EntryTypes.METAL
{
    public static class XAU_InstrumentMatrix
    {
        private static readonly Dictionary<string, XAU_InstrumentProfile> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["XAUUSD"] = new XAU_InstrumentProfile
                {
                    Symbol = "XAUUSD",

                    MinAtrPips = 10.0,
                    MinAdxTrend = 12.0,
                    MaxWickRatio = 0.75,

                    RangeLookbackBars = 14,
                    RangeMaxWidthAtr = 1.25,

                    AllowAsian = false,
                    SessionStartHour = 7,
                    SessionEndHour = 20,

                    SlAtrMultLow = 3.0,
                    SlAtrMultNormal = 2.7,
                    SlAtrMultHigh = 2.4,

                    Tp1R_High = 0.55,
                    Tp1R_Normal = 0.45,
                    Tp1R_Low = 0.40,

                    BeOffsetR = 0.10,

                    TrailAtrTight = 1.8,
                    TrailAtrNormal = 2.4,
                    TrailAtrLoose = 3.0,
                    MinTrailImprovePips = 25,

                    LotCap = 2.0
                },

                ["XAGUSD"] = new XAU_InstrumentProfile
                {
                    Symbol = "XAGUSD",

                    MinAtrPips = 8.0,
                    MinAdxTrend = 16.0,
                    MaxWickRatio = 0.70,

                    RangeLookbackBars = 14,
                    RangeMaxWidthAtr = 1.15,

                    AllowAsian = false,
                    SessionStartHour = 7,
                    SessionEndHour = 20,

                    SlAtrMultLow = 3.4,
                    SlAtrMultNormal = 3.0,
                    SlAtrMultHigh = 2.6,

                    Tp1R_High = 0.50,
                    Tp1R_Normal = 0.40,
                    Tp1R_Low = 0.35,

                    BeOffsetR = 0.10,

                    TrailAtrTight = 2.0,
                    TrailAtrNormal = 2.6,
                    TrailAtrLoose = 3.2,
                    MinTrailImprovePips = 20,

                    LotCap = 2.0
                }
            };

        public static XAU_InstrumentProfile Get(string symbolName)
        {
            if (string.IsNullOrWhiteSpace(symbolName))
                return _map["XAUUSD"];

            if (_map.TryGetValue(symbolName, out var p))
                return p;

            return _map["XAUUSD"];
        }
    }
}
