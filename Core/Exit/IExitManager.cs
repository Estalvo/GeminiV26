using cAlgo.API;

namespace GeminiV26.Core.Exit
{
    public interface IExitManager
    {
        /// <summary>
        /// Új pozíció regisztrálása entry után.
        /// </summary>
        void RegisterContext(PositionContext ctx);

        /// <summary>
        /// Tick-alapú exit kezelés (TP1, trailing, BE, early exit).
        /// Csak ott hívódik, ahol valóban szükséges.
        /// </summary>
        void OnTick();

        /// <summary>
        /// Bar-alapú exit kezelés (ritka, instrument-függő).
        /// </summary>
        void OnBar();

        /// <summary>
        /// Élő pozíciók visszatöltése restart után.
        /// </summary>
        void RehydrateFromLivePositions();

        /// <summary>
        /// Pozíció teljes lezárásakor takarítás.
        /// </summary>
        void Unregister(long positionId);
    }
}
