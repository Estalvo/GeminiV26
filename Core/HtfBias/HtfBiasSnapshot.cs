using GeminiV26.Core.Entry;

namespace GeminiV26.Core.HtfBias
{
    public sealed class HtfBiasSnapshot
    {
        public HtfBiasState State { get; set; } = HtfBiasState.Neutral;
        public TradeDirection AllowedDirection { get; set; } = TradeDirection.None;

        // 0..1 – debug / későbbi risk cap célra
        public double Confidence01 { get; set; } = 0.0;

        public string Reason { get; set; } = string.Empty;

        // Update gating (FX mintára)
        public System.DateTime LastUpdateH1Closed { get; set; }
    }
}
