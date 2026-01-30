using cAlgo.API;
using GeminiV26.Core;

namespace GeminiV26.Interfaces
{
    public interface IExitManager
    {
        void OnBar(Position position);
        void OnTick();
        // Context bekötés TP1/BE/trailing életre keltéséhez
        void RegisterContext(PositionContext ctx);
    }
}
