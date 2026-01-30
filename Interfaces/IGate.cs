using cAlgo.API;

namespace GeminiV26.Interfaces
{
    public interface IGate
    {
        bool AllowEntry(TradeType direction);
    }
}
