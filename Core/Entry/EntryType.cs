namespace GeminiV26.Core.Entry
{
    public enum EntryType
    {
        // ===== METAL =====
        XAU_Pullback,   // ⭐ EZ HIÁNYZIK
        XAU_Impulse,
        XAU_Flag,
        XAU_Reversal,

        // ===== FX =====
        FX_Pullback,
        FX_Flag,
        FX_RangeBreakout,
        FX_Reversal,
        FX_ImpulseContinuation,

        // ===== INDEX =====
        Index_Breakout,
        Index_Pullback,
        Index_Flag,

        // ===== CRYPTO =====
        Crypto_Impulse,
        Crypto_Flag,
        Crypto_Pullback,
        Crypto_RangeBreakout,

        // ===== LEGACY =====
        TC_Flag,
        TC_Pullback,
        BR_RangeBreakout,
        TR_Reversal
    }
}
