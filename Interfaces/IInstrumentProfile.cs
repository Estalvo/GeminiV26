using cAlgo.API;

namespace GeminiV26.Interfaces
{
    public interface IInstrumentProfile
    {
        double RiskPercent { get; }
        double InitialStopLossPips { get; }
    }
}
