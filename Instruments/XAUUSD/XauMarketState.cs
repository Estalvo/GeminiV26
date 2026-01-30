namespace GeminiV26.Instruments.XAUUSD
{
    public sealed class XauMarketState
    {
        // === ÚJ MODELL (MEGMARAD) ===
        public double AtrPips { get; set; }
        public double Adx { get; set; }

        public bool IsLowVol { get; set; }
        public bool IsTrend { get; set; }

        public double RangeWidthAtr { get; set; }
        public bool IsHardRange { get; set; }

        public double WickRatioNow { get; set; }
        public bool IsChop { get; set; }

        // === KOMPATIBILITÁSI ALIASOK (TRADECORE MIATT) ===

        // régi ATR
        public double Atr => AtrPips;

        // régi range logika
        public bool IsRange => IsHardRange;
        public bool IsSoftRange => IsLowVol;
        public bool IsBreakout => IsTrend;
        public bool IsPostBreakout => false; // XAU-nál nincs klasszikus postBO

        public double RangeWidth => RangeWidthAtr;

        // volume proxy (XAU-nál nincs tick volume)
        public double VolumeNorm => WickRatioNow;
    }
}
