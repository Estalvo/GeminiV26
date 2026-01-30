using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Entry;

namespace GeminiV26.Instruments
{
    /// <summary>
    /// Phase 3.4+
    /// Instrument-specifikus PositionContext feltöltés.
    /// TradeCore NEM számol, csak meghívja.
    /// </summary>
    public interface IInstrumentContextBuilder
    {
        /// <summary>
        /// Feltölti a PositionContext-et belépési döntés alapján.
        /// </summary>
        /// <param name="ctx">üres PositionContext</param>
        /// <param name="entry">kiválasztott entry (score, type, reason)</param>
        /// <param name="symbol">aktuális symbol</param>
        void Build(
            PositionContext ctx,
            EntryEvaluation entry
        );
    }
}
