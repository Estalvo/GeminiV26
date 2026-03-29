using cAlgo.API;

namespace GeminiV26.Core.Memory
{
    public class MemoryLogger
    {
        private readonly Robot _bot;

        public MemoryLogger(Robot bot)
        {
            _bot = bot;
        }

        public void LogWrite(TradeMemoryRecord record)
        {
            if (record == null)
                return;

            GlobalLogger.Log(
                "[MEM][WRITE]\n" +
                $"Instrument={record.Instrument}\n" +
                $"EntryType={record.EntryType}\n" +
                $"R={record.RMultiple:F2}\n" +
                $"MFE={record.MFE:F2}\n" +
                $"MAE={record.MAE:F2}\n" +
                $"Regime={record.MarketRegime}");
        }
    }
}
