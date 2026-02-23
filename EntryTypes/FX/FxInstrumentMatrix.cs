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
                        { FxSession.Asia,    -10 },
                        { FxSession.London,  +2 },
                        { FxSession.NewYork, +2 }
                    },
                    new()
                    {
                        { FxSession.Asia,    20 },
                        { FxSession.London,  23 },
                        { FxSession.NewYork, 25 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 68,               // SZIGORÍTÁS: 55-ről 62-re, kiöljük a bizonytalan trade-eket
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.2,        // SZIGORÍTÁS: 1.6-ról 1.2-re, ne engedjük a túlnyúlt zászlókat
                        MaxPullbackAtr = 0.80,       // SZIGORÍTÁS: 0.95-ről 0.80-ra, csak szoros visszateszt
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 12,    // SZIGORÍTÁS: 6-ról 12-re, precíz gyertyák kellenek
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // ÚJ: Ázsiában kötelező az M1 trigger!
                        AtrExpansionHardBlock = true // ÚJ: Blokkoljuk a hirtelen kiugrást
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 60,               // SZIGORÍTÁS: 55-ről 58-ra
                        FlagBars = 3,                // STABILITÁS: 3-ról 4 bárra emelve, több idő a bázisépítésre
                        MaxFlagAtrMult = 1.8,        // SZIGORÍTÁS: 2.2-ről 1.6-ra (ez fogja meg a csúcson való vétel ellen!)
                        MaxPullbackAtr = 1.00,       // SZIGORÍTÁS: 1.30-ról 1.00-ra
                        BreakoutAtrBuffer = 0.10,    // BIZTONSÁG: Nagyobb buffer, hogy ne ugorjunk bele a hamis kitörésbe
                        BodyMisalignPenalty = 4,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // ÚJ: Londonban is kötelező az M1, hogy ne vegyünk csúcsot! -< vissza false-ra
                        AtrExpansionHardBlock = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 64,               // SZIGORÍTÁS: 55-ről 58-ra
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.6,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.4,
                        maxContinuationBarsSinceBreak: 3,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
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
                    new()
                    {
                        { FxSession.Asia,    22 },
                        { FxSession.London,  24 },
                        { FxSession.NewYork, 26 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 64,
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
                        BaseScore = 54,
                        MinScore = 58,
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.3,
                        MaxPullbackAtr = 1.35,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 60,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.3,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.7,
                        maxContinuationBarsSinceBreak: 4,
                        requireHtfAlignmentForContinuation: false,
                        allowContinuationDuringHtfTransition: true
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
                        { FxSession.Asia,    -5 }, // ÚJ: Hiába Asia bias, büntessük meg a JPY kockázat miatt
                        { FxSession.London,   0 },
                        { FxSession.NewYork, -5 }  // SZIGORÍTÁS: -2-ről -5-re (NY nyitáskor veszélyes)
                    },
                    new()
                    {
                        { FxSession.Asia,    22 },
                        { FxSession.London,  24 },
                        { FxSession.NewYork, 26 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 70,               // SZIGORÍTÁS: 55-ről 62-re (csak a betonbiztos setupok)
                        FlagBars = 3,                // STABILITÁS: 2-ről 3 bárra
                        MaxFlagAtrMult = 1.1,        // SZIGORÍTÁS: 1.4-ről 1.1-re (csak szoros zászlók)
                        MaxPullbackAtr = 0.70,       // SZIGORÍTÁS: 0.95-ről 0.70-re (ne engedjünk mély visszatesztet)
                        BreakoutAtrBuffer = 0.08,    // BIZTONSÁG: Nagyobb buffer a fals kitörések ellen
                        BodyMisalignPenalty = 15,    // SZIGORÍTÁS: 6-ról 15-re
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // KRITIKUS: Itt is kötelező az M1 trigger!
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 62,               // SZIGORÍTÁS: 55-ről 58-ra
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.6,        // SZIGORÍTÁS: 2.0-ról 1.6-ra
                        MaxPullbackAtr = 0.90,       // SZIGORÍTÁS: 1.30-ról 0.90-re
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // KRITIKUS: Londonban is kötelező az M1 trigger!
                        AtrExpansionHardBlock = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 66,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.2,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 8,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.5,
                        maxContinuationBarsSinceBreak: 3,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
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
                        { FxSession.Asia,    +5 },
                        { FxSession.London,  +5 },
                        { FxSession.NewYork, -5 }
                    },
                    new()
                    {
                        { FxSession.Asia,    22 },
                        { FxSession.London,  24 },
                        { FxSession.NewYork, 26 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 66,               // SZIGORÍTÁS: 55 helyett 60, csak a legjobb setupok menjenek át
                        FlagBars = 3,                // STABILITÁS: 2 helyett 3 bár, hogy a zajt jobban szűrjük
                        MaxFlagAtrMult = 1.2,        // SZIGORÍTÁS: Kisebb zászló kiterjedés
                        MaxPullbackAtr = 1.20,       // LAZÍTÁS: 1.00-ról 1.20-ra - kell a tér a GBPJPY zajának
                        BreakoutAtrBuffer = 0.08,    // TÁVOLSÁG: Nagyobb buffer a kitörésnél
                        BodyMisalignPenalty = 10,    // SZIGORÍTÁS: 6-ról 10-re, ne engedjük a pontatlan gyertyákat
                        M1TriggerBonus = 2,
                        FlagQualityBonus = 3,        // ÚJ: Bónusz a minőségi alakzatnak
                        RequireM1Trigger = true,     // BIZTONSÁG: Ázsiában kötelező az M1 visszaigazolás
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 58,
                        FlagBars = 3,
                        MaxFlagAtrMult = 2.1,
                        MaxPullbackAtr = 1.40,
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = false,
                        AtrExpansionHardBlock = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 62,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.2,
                        MaxPullbackAtr = 0.95,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.8,
                        maxContinuationBarsSinceBreak: 5,
                        requireHtfAlignmentForContinuation: false,
                        allowContinuationDuringHtfTransition: true
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
                    { FxSession.Asia,    -5 }, // ÚJ: Minimális büntetés Ázsiára is a JPY kockázat miatt
                    { FxSession.London,  -5 },
                    { FxSession.NewYork, -20 }
                },
                new()
                {
                    { FxSession.Asia,    22 },
                    { FxSession.London,  24 },
                    { FxSession.NewYork, 26 }
                },
                asia: new FxFlagSessionTuning
                {
                    BaseScore = 52,
                    MinScore = 68,               // SZIGORÍTÁS: 55-ről 60-ra (csak a legtisztább EMA21 bounce mehet)
                    FlagBars = 3,                // STABILITÁS: Több megerősítő gyertya kell
                    MaxFlagAtrMult = 1.2,        // SZIGORÍTÁS: 1.3-ról 1.2-re (kisebb zászló = kisebb kockázat)
                    MaxPullbackAtr = 0.80,       // SZIGORÍTÁS: 0.85-ről 0.80-ra
                    BreakoutAtrBuffer = 0.08,
                    BodyMisalignPenalty = 15,    // SZIGORÍTÁS: 10-ről 15-re (precízebb gyertyatestek)
                    M1TriggerBonus = 4,
                    FlagQualityBonus = 3,
                    RequireM1Trigger = true,
                    AtrExpansionHardBlock = true,
                    RequireAtrSlopePositive = true,
                    RequireStrongEntryCandle = true
                },
                london: new FxFlagSessionTuning
                {
                    BaseScore = 54,
                    MinScore = 62,               // SZIGORÍTÁS: Londonban is magasabb küszöb
                    FlagBars = 3,
                    MaxFlagAtrMult = 1.8,        // SZIGORÍTÁS: 2.0-ról 1.8-ra (kevesebb zajt engedünk)
                    MaxPullbackAtr = 1.10,       // SZIGORÍTÁS: 1.30-ról 1.10-re (ne engedjünk túl mély korrekciót)
                    BreakoutAtrBuffer = 0.08,
                    BodyMisalignPenalty = 5,
                    M1TriggerBonus = 5,
                    FlagQualityBonus = 3,
                    RequireM1Trigger = true,     // KRITIKUS: Londonban is kötelezővé tesszük az M1 triggert!
                    AtrExpansionHardBlock = true // ÚJ: Itt is bekapcsoljuk a blokkolást hirtelen tágulásnál
                },
                ny: new FxFlagSessionTuning
                {
                    // New York marad a -20-as bias miatt eleve tiltáshoz közeli állapotban
                    BaseScore = 54,
                    MinScore = 70,
                    FlagBars = 2,
                    MaxFlagAtrMult = 1.1,
                    MaxPullbackAtr = 0.75,
                    BreakoutAtrBuffer = 0.10,
                    BodyMisalignPenalty = 8,
                    M1TriggerBonus = 0,
                    FlagQualityBonus = 3,
                    RequireM1Trigger = true,
                    AtrExpansionHardBlock = true,
                    RequireAtrSlopePositive = true,
                    RequireStrongEntryCandle = true
                },
                // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.6,
                        maxContinuationBarsSinceBreak: 3,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
            ),
                ["AUDUSD"] = Build(
                    "AUDUSD",
                    FxVolatilityClass.Low,
                    FxSessionBias.Asia,
                    FxPullbackStyle.EMA21,
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
                        { FxSession.Asia,    -5 }, // ÚJ: Itt is bevezetünk egy kis szigort
                        { FxSession.London, -15 }, // SZIGORÍTÁS: -10 helyett -15
                        { FxSession.NewYork,-25 }  // SZIGORÍTÁS: -20 helyett -25
                    },
                    new()
                    {
                        { FxSession.Asia,    16 },
                        { FxSession.London,  18 },
                        { FxSession.NewYork, 20 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 68,               // SZIGORÍTÁS: 55-ről 60-ra
                        FlagBars = 3,                // STABILITÁS: 2 helyett 3 bár
                        MaxFlagAtrMult = 1.1,        // SZIGORÍTÁS: 1.4-ről 1.1-re (ne vegyünk túlnyúlt mozgást)
                        MaxPullbackAtr = 0.65,       // SZIGORÍTÁS: 0.75-ről 0.65-re
                        BreakoutAtrBuffer = 0.10,    // BIZTONSÁG: Nagyobb puffer
                        BodyMisalignPenalty = 15,    // SZIGORÍTÁS: 10-ről 15-re (csak szép gyertyák)
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true // KRITIKUS: Mostantól kötelező az erős belépő gyertya!
                    },
                    london: new FxFlagSessionTuning
                    {
                        // London marad szigorú, de itt is bekapcsoljuk a biztonsági fékeket
                        BaseScore = 54,
                        MinScore = 64,
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.2,
                        MaxPullbackAtr = 0.70,
                        BreakoutAtrBuffer = 0.12,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 72,               // NY-ban szinte elérhetetlen szigor
                        FlagBars = 1,
                        MaxFlagAtrMult = 0.9,
                        MaxPullbackAtr = 0.60,
                        BreakoutAtrBuffer = 0.15,
                        BodyMisalignPenalty = 8,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = false
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.3,
                        maxContinuationBarsSinceBreak: 2,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
                ),

                ["NZDUSD"] = Build(
                    "NZDUSD",
                    FxVolatilityClass.Low,
                    FxSessionBias.Asia,
                    FxPullbackStyle.EMA21,
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
                        { FxSession.Asia,    -5 }, // ÚJ: Minimális szigorítás
                        { FxSession.London, -15 }, // SZIGORÍTÁS: -10 -> -15
                        { FxSession.NewYork,-35 }  // SZIGORÍTÁS: -30 -> -35
                    },
                    new()
                    {
                        { FxSession.Asia,    16 },
                        { FxSession.London,  18 },
                        { FxSession.NewYork, 20 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 68,               // SZIGORÍTÁS: 55 -> 60
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.1,        // SZIGORÍTÁS: 1.7 -> 1.1 (csak kompakt alakzatok)
                        MaxPullbackAtr = 0.70,       // SZIGORÍTÁS: 1.05 -> 0.70
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 12,    // SZIGORÍTÁS: 6 -> 12
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // ÚJ: Kötelező M1 visszaigazolás
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true // ÚJ: Ne vegyen "gyenge" gyertyával
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 64,
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.3,        // SZIGORÍTÁS: 1.8 -> 1.3
                        MaxPullbackAtr = 0.85,       // SZIGORÍTÁS: 1.35 -> 0.85
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // ÚJ: Itt is kell az M1 kontroll
                        AtrExpansionHardBlock = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 74,
                        FlagBars = 2,
                        MaxFlagAtrMult = 0.9,
                        MaxPullbackAtr = 0.85,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 8,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.3,
                        maxContinuationBarsSinceBreak: 2,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
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
                        { FxSession.Asia,    -5 },  // SZIGORÍTÁS: Ne legyen ingyen a belépés Ázsiában sem
                        { FxSession.London,  -20 }, // SZIGORÍTÁS: Londonban ez a pár csak zaj
                        { FxSession.NewYork, -40 }  // TILTÁS KÖZELI: New Yorkban értelmezhetetlen
                    },
                    new()
                    {
                        { FxSession.Asia,    16 },
                        { FxSession.London,  18 },
                        { FxSession.NewYork, 20 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 72,               // SZIGORÍTÁS: 55 -> 62 (legyen nagyon válogatós)
                        FlagBars = 4,                // STABILITÁS: 2 -> 4 bár (építsen komoly bázist)
                        MaxFlagAtrMult = 1.0,        // SZIGORÍTÁS: 1.8 -> 1.0 (csak kompakt, sűrű bázis)
                        MaxPullbackAtr = 0.60,       // SZIGORÍTÁS: 1.10 -> 0.60 (ne engedjünk mély visszatesztet)
                        BreakoutAtrBuffer = 0.10,    // BIZTONSÁG: Nagyobb tizedelés a kitörésnél
                        BodyMisalignPenalty = 20,    // SZIGORÍTÁS: 6 -> 20 (csak tökéletes gyertyasorrend)
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 5,        // ÚJ: Extra bónusz, ha vizuálisan tiszta a setup
                        RequireM1Trigger = true,     // KRITIKUS: Itt kötelező az M1
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 75,               // Londonban szinte csak "véletlenül" lépjen be
                        FlagBars = 4,
                        MaxFlagAtrMult = 1.1,
                        MaxPullbackAtr = 0.70,
                        BreakoutAtrBuffer = 0.12,
                        BodyMisalignPenalty = 10,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 80,               // Gyakorlatilag kikapcsolva
                        FlagBars = 2,
                        MaxFlagAtrMult = 0.7,
                        MaxPullbackAtr = 0.50,
                        BreakoutAtrBuffer = 0.15,
                        BodyMisalignPenalty = 12,
                        M1TriggerBonus = 0,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.2,
                        maxContinuationBarsSinceBreak: 2,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
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
                        { FxSession.Asia,    -10 }, // SZIGORÍTÁS: -5 -> -10 (Ázsiában csak a CAD "véletlen" mozgásai vannak)
                        { FxSession.London,   0 },
                        { FxSession.NewYork, +5 }   // JUTALMAZÁS: +2 -> +5 (Ez a pár itt él igazán)
                    },
                    new()
                    {
                        { FxSession.Asia,    20 },
                        { FxSession.London,  23 },
                        { FxSession.NewYork, 25 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 68,               // SZIGORÍTÁS: 55 -> 60
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.2,        // SZIGORÍTÁS: 1.4 -> 1.2
                        MaxPullbackAtr = 0.75,       // SZIGORÍTÁS: 0.85 -> 0.75
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 10,    // SZIGORÍTÁS: 6 -> 10
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true // ÚJ: Kell a lendület!
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 62,               // SZIGORÍTÁS: 55 -> 58
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.6,        // SZIGORÍTÁS: 2.0 -> 1.6 (megfogja a csúcson vételt)
                        MaxPullbackAtr = 1.00,       // SZIGORÍTÁS: 1.30 -> 1.00
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // ÚJ: Itt is kötelező az M1 trigger
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true // ÚJ: Kell a lendület!
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 58,               // NY-ban maradhat a 55, mert a +5 bónusszal együtt is szűrve lesz
                        FlagBars = 3,                // STABILITÁS: 2 -> 3 bár
                        MaxFlagAtrMult = 1.3,
                        MaxPullbackAtr = 0.90,
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 2,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true // ÚJ: Itt is kötelező
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.6,
                        maxContinuationBarsSinceBreak: 4,
                        requireHtfAlignmentForContinuation: false,
                        allowContinuationDuringHtfTransition: true
                ),

                ["USDCHF"] = Build(
                    "USDCHF",
                    FxVolatilityClass.Low,
                    FxSessionBias.London,
                    FxPullbackStyle.EMA21,
                    60,
                    0.8,
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
                        { FxSession.Asia,    -15 }, // SZIGORÍTÁS: -5 -> -15 (Ázsiában tiltsuk le szinte teljesen)
                        { FxSession.London,  +4 },
                        { FxSession.NewYork, +2 }   // JUTALMAZÁS: 0 -> +2 (NY-ban is tud szépen mozogni)
                    },
                    new()
                    {
                        { FxSession.Asia,    20 },
                        { FxSession.London,  23 },
                        { FxSession.NewYork, 25 }
                    },
                    asia: new FxFlagSessionTuning
                    {
                        BaseScore = 52,
                        MinScore = 75,               // SZIGORÍTÁS: 55 -> 65 (Csak extrém jó jel mehet át)
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.0,        // SZIGORÍTÁS: 1.4 -> 1.0 (Csak nagyon szűk bázis)
                        MaxPullbackAtr = 0.60,       // SZIGORÍTÁS: 0.95 -> 0.60
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 15,    // SZIGORÍTÁS: 6 -> 15
                        M1TriggerBonus = 4,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,     // ÚJ: Kötelező M1
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    london: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 62,               // SZIGORÍTÁS: 55 -> 58
                        FlagBars = 3,
                        MaxFlagAtrMult = 1.4,        // SZIGORÍTÁS: 2.0 -> 1.4 (Kompaktabb setupok)
                        MaxPullbackAtr = 0.85,       // SZIGORÍTÁS: 1.30 -> 0.85
                        BreakoutAtrBuffer = 0.08,
                        BodyMisalignPenalty = 5,
                        M1TriggerBonus = 5,
                        FlagQualityBonus = 5,        // JUTALMAZÁS: Ha tiszta a kép, kapjon nagy bónuszt
                        RequireM1Trigger = true,     // ÚJ: Londonban is kötelező az M1 trigger
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    ny: new FxFlagSessionTuning
                    {
                        BaseScore = 54,
                        MinScore = 64,
                        FlagBars = 2,
                        MaxFlagAtrMult = 1.1,
                        MaxPullbackAtr = 0.75,       // SZIGORÍTÁS: 0.90 -> 0.75
                        BreakoutAtrBuffer = 0.10,
                        BodyMisalignPenalty = 6,
                        M1TriggerBonus = 2,
                        FlagQualityBonus = 3,
                        RequireM1Trigger = true,
                        AtrExpansionHardBlock = true,
                        RequireAtrSlopePositive = true,
                        RequireStrongEntryCandle = true
                    },
                    // ===== CONTINUATION CHARACTER =====
                        maxContinuationRatr: 1.3,
                        maxContinuationBarsSinceBreak: 3,
                        requireHtfAlignmentForContinuation: true,
                        allowContinuationDuringHtfTransition: false
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
            double minAtr,
            Dictionary<FxSession, int> sessionScore,
            Dictionary<FxSession, double> minAdxBySession,
            FxFlagSessionTuning? asia = null,
            FxFlagSessionTuning? london = null,
            FxFlagSessionTuning? ny = null,

            // ==================================================
            // CONTINUATION CHARACTER (instrument-level override)
            // ==================================================
            double maxContinuationRatr = 1.5,
            int maxContinuationBarsSinceBreak = 4,
            bool requireHtfAlignmentForContinuation = false,
            bool allowContinuationDuringHtfTransition = true
        )
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
                MinAdxBySession = minAdxBySession,
                MinAtrPips = minAtr,
                SessionScoreDelta = sessionScore,
                FlagTuning = new Dictionary<FxSession, FxFlagSessionTuning>
                {
                    { FxSession.Asia, asia ?? throw new ArgumentNullException(nameof(asia)) },
                    { FxSession.London, london ?? throw new ArgumentNullException(nameof(london)) },
                    { FxSession.NewYork, ny ?? throw new ArgumentNullException(nameof(ny)) }
            },

                // ==================================================
                // CONTINUATION CHARACTER (NOW MATRIX-DRIVEN)
                // ==================================================
                MaxContinuationRatr = maxContinuationRatr,
                MaxContinuationBarsSinceBreak = maxContinuationBarsSinceBreak,
                RequireHtfAlignmentForContinuation = requireHtfAlignmentForContinuation,
                AllowContinuationDuringHtfTransition = allowContinuationDuringHtfTransition
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
