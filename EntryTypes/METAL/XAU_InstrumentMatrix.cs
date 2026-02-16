using System;
using System.Collections.Generic;
using GeminiV26.Core;
using GeminiV26.Core.Entry;
using GeminiV26.Instruments.FX;

namespace GeminiV26.EntryTypes.METAL
{
    public static class XAU_InstrumentMatrix
    {
        private static readonly Dictionary<string, XAU_InstrumentProfile> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["XAUUSD"] = Build(
                    symbol: "XAUUSD",
                    
                    // ===== instrument karakter =====
                    pullbackStyle: FxPullbackStyle.EMA50,
                    minAtrPips: 10.0,
                    minAdx: 18.0,
                    maxWick: 0.52,
                    rangeLookback: 14,
                    rangeMaxAtr: 0.95,
                    allowAsia: false,
                    sessionStart: 7,
                    sessionEnd: 20,

                    // ===== entry policy =====
                    maxFlagAtr: 2.0,
                    maxPullbackAtr: 1.20,
                    atrExpansionHardBlock: true,
                    atrExpandPenalty: 8,

                    sessionScore: new()
                    {
                        { FxSession.Asia,    -8 },
                        { FxSession.London,  +6 },
                        { FxSession.NewYork, +4 }
                    },

                    // ===== FLAG SESSION TUNING =====
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 55,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,      // ⬆️ 1.3 → 1.4 (kicsit több levegő)
                        MaxPullbackAtr = 0.90,     // ⬆️ 0.85 → 0.90
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 3,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false,
                        MinScore = 68              // marad magas
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 62,
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.0,      // ⬆️ 2.2 → 2.8
                        MaxPullbackAtr = 1.05,     // ⬆️ 1.30 → 1.40
                        BreakoutAtrBuffer = 0.07, // ⬇️ 0.08 → 0.07
                        BodyMisalignPenalty = 4,
                        M1TriggerBonus = 6,
                        FlagQualityBonus = 4,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false,
                        MinScore = 65              // ⬇️ 70 → 65 (nagyon fontos!)
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 58,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.6,      // ⬆️ 1.5 → 1.8
                        MaxPullbackAtr = 1.20,     // ⬆️ 1.05 → 1.70
                        BreakoutAtrBuffer = 0.09, // ⬇️ 0.10 → 0.09
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 6,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        MinScore = 62              // marad
                    }
                ),

                ["XAGUSD"] = Build(
                    symbol: "XAGUSD",

                    pullbackStyle: FxPullbackStyle.Structure,
                    minAtrPips: 8.0,
                    minAdx: 18.0,
                    maxWick: 0.55,
                    rangeLookback: 14,
                    rangeMaxAtr: 1.00,
                    allowAsia: false,
                    sessionStart: 7,
                    sessionEnd: 20,

                    maxFlagAtr: 2.2,
                    maxPullbackAtr: 1.30,
                    atrExpansionHardBlock: true,
                    atrExpandPenalty: 10,

                    sessionScore: new()
                    {
                        { FxSession.Asia,    -10 },
                        { FxSession.London,  +5 },
                        { FxSession.NewYork, +3 }
                    },

                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 55,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 3,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 60,
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.2,
                        MaxPullbackAtr = 1.15,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false,
                        MinScore = 65
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 50,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.3,
                        MaxPullbackAtr = 1.00,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        MinScore = 58
                    }
                )
            };

        // =====================================================
        // DEBUG / REGISTRY API (FX-MINTA)
        // =====================================================
        public static IEnumerable<string> DebugKeys() => _map.Keys;

        // =====================================================
        // BUILDER – KÖTELEZŐ KONZISZTENCIA
        // =====================================================
        private static XAU_InstrumentProfile Build(
            string symbol,
            FxPullbackStyle pullbackStyle,
            double minAtrPips,
            double minAdx,
            double maxWick,
            int rangeLookback,
            double rangeMaxAtr,
            bool allowAsia,
            int sessionStart,
            int sessionEnd,
            double maxFlagAtr,
            double maxPullbackAtr,
            bool atrExpansionHardBlock,
            int atrExpandPenalty,
            Dictionary<FxSession, int> sessionScore,
            FxFlagSessionTuning asia,
            FxFlagSessionTuning london,
            FxFlagSessionTuning ny)
        {
            return new XAU_InstrumentProfile
            {
                Symbol = symbol,

                // ===== MarketState =====
                MinAtrPips = minAtrPips,
                MinAdxTrend = minAdx,
                MaxWickRatio = maxWick,
                RangeLookbackBars = rangeLookback,
                RangeMaxWidthAtr = rangeMaxAtr,

                // ===== Session gate =====
                AllowAsian = allowAsia,
                SessionStartHour = sessionStart,
                SessionEndHour = sessionEnd,

                // ===== ENTRY policy =====
                PullbackStyle = pullbackStyle,
                MaxFlagAtrMult = maxFlagAtr,
                MaxPullbackAtr = maxPullbackAtr,
                AtrExpansionHardBlock = atrExpansionHardBlock,
                AtrExpandPenalty = atrExpandPenalty,
                SessionScoreDelta = sessionScore,

                // ===== FLAG session tuning =====
                FlagTuning = new()
                {
                    [FxSession.Asia] = asia,
                    [FxSession.London] = london,
                    [FxSession.NewYork] = ny
                }
            };
        }

        // =====================================================
        // SAFE ACCESSORS
        // =====================================================
        public static XAU_InstrumentProfile Get(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            var s = symbol.ToUpperInvariant();
            if (!s.Contains("XAU") && !s.Contains("XAG"))
                return null;

            if (_map.TryGetValue(symbol, out var profile))
                return profile;

            return null;
        }

        public static bool Contains(string symbol)
            => _map.ContainsKey(symbol);
    }
}
