using System.Collections.Generic;

namespace GeminiV26.Core.Memory
{
    public class TradeMemoryStore
    {
        private readonly List<TradeMemoryRecord> _records = new();

        public void AddRecord(TradeMemoryRecord record)
        {
            if (record == null)
                return;

            _records.Add(record);
        }

        public IReadOnlyList<TradeMemoryRecord> GetAll()
        {
            return _records;
        }
    }
}
