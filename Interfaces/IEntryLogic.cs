using cAlgo.API;

namespace GeminiV26.Interfaces
{
    // =========================================================
    // GEMINI V26 – EntryLogic Interface
    // Phase 3.7.3 – RULEBOOK 1.0 COMPLIANT
    //
    // SZEREP:
    // - Instrument-specifikus belépési LOGIKA
    // - Setup értékelés, bias és LogicConfidence számítása
    //
    // FONTOS:
    // - NEM gate
    // - NEM risk döntő
    // - NEM SL/TP döntő
    //
    // Az EntryLogic kizárólag INFORMÁCIÓT ad.
    // =========================================================
    public interface IEntryLogic
    {
        /// <summary>
        /// Kiértékeli a piaci környezetet.
        /// NEM tilt, NEM dönt trade indításról.
        /// </summary>
        void Evaluate();

        /// <summary>
        /// Utolsó kiértékelt irány (bias).
        /// </summary>
        TradeType LastBias { get; }

        /// <summary>
        /// Logikai confidence (0–100).
        /// Nem entry score.
        /// </summary>
        int LastLogicConfidence { get; }
    }
}
