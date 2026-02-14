public class FxFlagSessionTuning
{    
    public int FlagBars { get; set; }
    public double MaxFlagAtrMult { get; set; }
    public double MaxPullbackAtr { get; set; }
    public double BreakoutAtrBuffer { get; set; }
    public int BodyMisalignPenalty { get; set; }
    public int M1TriggerBonus { get; set; }
    public int FlagQualityBonus { get; set; }
    public bool RequireM1Trigger { get; set; }
    public bool AtrExpansionHardBlock { get; set; }

    // ✅ HIÁNYZÓK – IDE KELLENEK
    public int BaseScore { get; set; } = 20;
    public int MinScore { get; set; } = 45;

    public int MaxBarsSinceImpulse { get; set; } = 6;

    public int HtfBasePenalty { get; set; } = 10;
    public int HtfScalePenalty { get; set; } = 15;
}
