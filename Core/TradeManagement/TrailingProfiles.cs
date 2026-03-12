using System;
using GeminiV26.Core;

namespace GeminiV26.Core.TradeManagement
{
    public sealed class TrailingProfile
    {
        public string Name { get; init; } = "Default";
        public double StructureBufferAtr { get; init; }
        public double AtrMultiplierLowVol { get; init; }
        public double AtrMultiplierNormal { get; init; }
        public double AtrMultiplierHighVol { get; init; }
        public double DefensiveAtrMultiplier { get; init; }
        public double MinSlUpdateDeltaPips { get; init; }
        public bool AllowLiquidityTrailing { get; init; }
        public bool AllowTp2Extension { get; init; }
        public double TrendTp2ExtensionMultiplier { get; init; }
        public double StrongTrendTp2ExtensionMultiplier { get; init; }
    }

    public static class TrailingProfiles
    {
        public static TrailingProfile Fx => new()
        {
            Name = "FX",
            StructureBufferAtr = 0.35,
            AtrMultiplierLowVol = 1.10,
            AtrMultiplierNormal = 1.45,
            AtrMultiplierHighVol = 1.80,
            DefensiveAtrMultiplier = 1.95,
            MinSlUpdateDeltaPips = 1.0,
            AllowLiquidityTrailing = false,
            AllowTp2Extension = true,
            TrendTp2ExtensionMultiplier = 1.22,
            StrongTrendTp2ExtensionMultiplier = 1.45
        };

        public static TrailingProfile Index => new()
        {
            Name = "INDEX",
            StructureBufferAtr = 0.45,
            AtrMultiplierLowVol = 1.35,
            AtrMultiplierNormal = 1.75,
            AtrMultiplierHighVol = 2.20,
            DefensiveAtrMultiplier = 2.40,
            MinSlUpdateDeltaPips = 3.0,
            AllowLiquidityTrailing = true,
            AllowTp2Extension = true,
            TrendTp2ExtensionMultiplier = 1.23,
            StrongTrendTp2ExtensionMultiplier = 1.48
        };

        public static TrailingProfile Crypto => new()
        {
            Name = "CRYPTO",
            StructureBufferAtr = 0.60,
            AtrMultiplierLowVol = 1.50,
            AtrMultiplierNormal = 2.05,
            AtrMultiplierHighVol = 2.75,
            DefensiveAtrMultiplier = 3.10,
            MinSlUpdateDeltaPips = 8.0,
            AllowLiquidityTrailing = true,
            AllowTp2Extension = true,
            TrendTp2ExtensionMultiplier = 1.25,
            StrongTrendTp2ExtensionMultiplier = 1.50
        };

        public static TrailingProfile Metal => new()
        {
            Name = "METAL",
            StructureBufferAtr = 0.50,
            AtrMultiplierLowVol = 1.25,
            AtrMultiplierNormal = 1.70,
            AtrMultiplierHighVol = 2.25,
            DefensiveAtrMultiplier = 2.50,
            MinSlUpdateDeltaPips = 4.0,
            AllowLiquidityTrailing = true,
            AllowTp2Extension = true,
            TrendTp2ExtensionMultiplier = 1.24,
            StrongTrendTp2ExtensionMultiplier = 1.46
        };

        public static TrailingProfile ResolveBySymbol(string symbolName)
        {
            return SymbolRouting.ResolveInstrumentClass(symbolName) switch
            {
                InstrumentClass.METAL => Metal,
                InstrumentClass.CRYPTO => Crypto,
                InstrumentClass.INDEX => Index,
                _ => Fx
            };
        }
    }
}
