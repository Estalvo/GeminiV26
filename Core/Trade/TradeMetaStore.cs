using System.Collections.Generic;

namespace GeminiV26.Core
{
    public class TradeMetaStore
    {
        private readonly Dictionary<string, PendingEntryMeta> _pending = new();
        private readonly Dictionary<long, PendingEntryMeta> _byPosition = new();

        public void RegisterPending(string symbol, PendingEntryMeta meta)
        {
            if (_pending.ContainsKey(symbol))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[META OVERWRITE WARNING] symbol={symbol}"
                );
            }

            _pending[symbol] = meta;
        }

        public bool BindToPosition(long positionId, string symbol)
        {
            if (_pending.TryGetValue(symbol, out var meta))
            {
                _byPosition[positionId] = meta;
                _pending.Remove(symbol);
                return true;
            }
            return false;
        }

        public bool TryGet(long positionId, out PendingEntryMeta meta)
        {
            return _byPosition.TryGetValue(positionId, out meta);
        }

        public void Remove(long positionId)
        {
            _byPosition.Remove(positionId);
        }
    }

    public class PendingEntryMeta
    {
        public string EntryType;
        public string EntryReason;
        public int? Confidence;
    }
}
