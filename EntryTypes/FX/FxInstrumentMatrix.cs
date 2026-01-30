using System;
using System.Collections.Generic;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.Instruments.FX
{
    public static class FxInstrumentMatrix
    {
        private static readonly Dictionary<string, FxInstrumentProfile> _map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["EURUSD"] = Build(
                    "EURUSD",
                    FxVolatilityClass.Low,
                    FxSessionBias.London,
                    FxPullbackStyle.EMA21,
                    70,
                    1.1,
                    0.30,
                    false,
                    true,
                    0.42,
                    0.15,
                    2.2,
                    15,
                    1.9,
                    new()
                    {
                        { FxSession.Asia,    -5 },
                        { FxSession.London,  +6 },
                        { FxSession.NewYork, +4 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.6,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.2,
                        MaxPullbackAtr = 1.30,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    }
                ),

                ["GBPUSD"] = Build(
                    "GBPUSD",
                    FxVolatilityClass.High,
                    FxSessionBias.London,
                    FxPullbackStyle.EMA21,
                    110,
                    1.4,
                    0.28,
                    false,
                    false,
                    0.48,
                    0.18,
                    2.2,
                    17,
                    2.3,
                    new()
                    {
                        { FxSession.Asia,    -10 },
                        { FxSession.London,  +6 },
                        { FxSession.NewYork, +3 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.5,
                        MaxPullbackAtr = 1.00,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.3,
                        MaxPullbackAtr = 1.35,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.3,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    }
                ),

                ["USDJPY"] = Build(
                    "USDJPY",
                    FxVolatilityClass.Medium,
                    FxSessionBias.Asia,
                    FxPullbackStyle.Shallow,
                    80,
                    1.0,
                    0.40,
                    true,
                    false,
                    0.40,
                    0.12,
                    2.0,
                    15,
                    1.6,
                    new()
                    {
                        { FxSession.Asia,     0 },
                        { FxSession.London,   0 },
                        { FxSession.NewYork, -2 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.0,
                        MaxPullbackAtr = 1.30,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.2,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    }
                ),

                ["GBPJPY"] = Build(
                    "GBPJPY",
                    FxVolatilityClass.High,
                    FxSessionBias.London,
                    FxPullbackStyle.Structure,
                    140,
                    1.8,
                    0.22,
                    false,
                    false,
                    0.52,
                    0.20,
                    2.2,
                    19,
                    3.2,
                    new()
                    {
                        { FxSession.Asia,    0 },
                        { FxSession.London,  +5 },
                        { FxSession.NewYork, -5 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 1.00,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.1,
                        MaxPullbackAtr = 1.40,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.2,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    }
                ),

                ["EURJPY"] = Build(
                    "EURJPY",
                    FxVolatilityClass.Medium,
                    FxSessionBias.Mixed,
                    FxPullbackStyle.EMA21,
                    90,
                    1.25,
                    0.32,
                    false,
                    false,
                    0.42,
                    0.15,
                    2.2,
                    16,
                    2.0,
                    new()
                    {
                        { FxSession.Asia,    0 },
                        { FxSession.London, -5 },
                        { FxSession.NewYork,-20 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.3,
                        MaxPullbackAtr = 0.85,
                        BreakoutAtrBuffer = 0.07,
                        BodyMisalignPenalty = 10,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.0,
                        MaxPullbackAtr = 1.30,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.1,
                        MaxPullbackAtr = 0.75,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    }
                ),

                ["AUDUSD"] = Build(
                    "AUDUSD",
                    FxVolatilityClass.Low,
                    FxSessionBias.Asia,
                    FxPullbackStyle.EMA50,
                    65,
                    0.7,
                    0.38,
                    true,
                    true,
                    0.35,
                    0.12,
                    2.0,
                    13,
                    1.4,
                    new()
                    {
                        { FxSession.Asia,    0 },
                        { FxSession.London, -10 },
                        { FxSession.NewYork,-20 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 0.75,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 10,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.5,
                        MaxPullbackAtr = 0.85,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 1,
                        MaxFlagAtrMult = 0.9,
                        MaxPullbackAtr = 0.60,
                        BreakoutAtrBuffer = 0.15,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    }
                ),

                ["NZDUSD"] = Build(
                    "NZDUSD",
                    FxVolatilityClass.Low,
                    FxSessionBias.Asia,
                    FxPullbackStyle.EMA50,
                    60,
                    0.7,
                    0.38,
                    true,
                    true,
                    0.35,
                    0.12,
                    2.0,
                    13,
                    1.3,
                    new()
                    {
                        { FxSession.Asia,    0 },
                        { FxSession.London, -10 },
                        { FxSession.NewYork,-30 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.7,
                        MaxPullbackAtr = 1.05,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.8,
                        MaxPullbackAtr = 1.35,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 0.9,
                        MaxPullbackAtr = 0.85,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    }
                ),

                ["AUDNZD"] = Build(
                    "AUDNZD",
                    FxVolatilityClass.VeryLow,
                    FxSessionBias.Asia,
                    FxPullbackStyle.Structure,
                    45,
                    0.7,
                    0.45,
                    true,
                    true,
                    0.30,
                    0.10,
                    2.0,
                    13,
                    1.0,
                    new()
                    {
                        { FxSession.Asia,    0 },
                        { FxSession.London, -10 },
                        { FxSession.NewYork,-30 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.8,
                        MaxPullbackAtr = 1.10,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.6,
                        MaxPullbackAtr = 1.20,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 0.8,
                        MaxPullbackAtr = 0.80,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    }
                ),

                ["USDCAD"] = Build(
                    "USDCAD",
                    FxVolatilityClass.Medium,
                    FxSessionBias.NewYork,
                    FxPullbackStyle.EMA21,
                    85,
                    1.1,
                    0.32,
                    false,
                    false,
                    0.42,
                    0.15,
                    2.2,
                    15,
                    1.9,
                    new()
                    {
                        { FxSession.Asia,    -5 },
                        { FxSession.London,   0 },
                        { FxSession.NewYork, +2 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 0.85,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.0,
                        MaxPullbackAtr = 1.30,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.3,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    }
                ),

                ["USDCHF"] = Build(
                    "USDCHF",
                    FxVolatilityClass.Low,
                    FxSessionBias.London,
                    FxPullbackStyle.EMA21,
                    60,
                    1.1,
                    0.32,
                    false,
                    false,
                    0.36,
                    0.12,
                    2.2,
                    15,
                    1.5,
                    new()
                    {
                        { FxSession.Asia,   -5 },
                        { FxSession.London, +4 },
                        { FxSession.NewYork, 0 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.4,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.05,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 0,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    london: new FxFlagSessionTuning
                    {
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.0,
                        MaxPullbackAtr = 1.30,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = false
                    },
                    ny: new FxFlagSessionTuning
                    {
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.1,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = int.MaxValue,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    }
                ),
            };

        public static IEnumerable<string> DebugKeys() => _map.Keys;

        private static FxInstrumentProfile Build(
            string symbol,
            FxVolatilityClass vol,
            FxSessionBias bias,
            FxPullbackStyle pb,
            double adr,
            double minImpulse,
            double maxWick,
            bool allowAsia,
            bool meanRev,
            double atrM5,
            double atrM1,
            double maxFlagAtr,
            double minAdx,
            double minAtr,
            Dictionary<FxSession, int> sessionScore,
            FxFlagSessionTuning asia,
            FxFlagSessionTuning london,
            FxFlagSessionTuning ny)
        {
            return new FxInstrumentProfile
            {
                Symbol = symbol,
                Volatility = vol,
                SessionBias = bias,
                PullbackStyle = pb,
                TypicalAdrPips = adr,
                MinImpulseAtr = minImpulse,
                MaxWickRatio = maxWick,
                AllowAsianSession = allowAsia,
                MeanReversionFriendly = meanRev,
                ImpulseAtrMult_M5 = atrM5,
                ImpulseAtrMult_M1 = atrM1,
                MaxFlagAtrMult = maxFlagAtr,
                MinAdxTrend = minAdx,
                MinAtrPips = minAtr,
                SessionScoreDelta = sessionScore,
                FlagTuning = new()
                {
                    [FxSession.Asia] = asia,
                    [FxSession.London] = london,
                    [FxSession.NewYork] = ny
                }
            };
        }

        public static FxInstrumentProfile Get(string symbol)
        {
            if (_map.TryGetValue(symbol, out var profile))
                return profile;

            throw new ArgumentException($"FX instrument not defined in matrix: {symbol}");
        }

        public static bool Contains(string symbol)
            => _map.ContainsKey(symbol);
    }
}
