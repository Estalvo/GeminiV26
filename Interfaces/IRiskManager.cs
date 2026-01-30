using cAlgo.API;

namespace GeminiV26.Interfaces
{
    public interface IRiskManager
    {
        long CalculateVolume(double riskPercent, double stopLossPips);
    }
}
