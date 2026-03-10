using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.Context
{
    public sealed class ContextRegistry
    {
        private readonly Dictionary<long, PositionContext> _positions = new Dictionary<long, PositionContext>();
        private readonly Dictionary<long, EntryContext> _entries = new Dictionary<long, EntryContext>();

        public void RegisterEntry(long positionId, EntryContext ctx)
        {
            if (positionId <= 0 || ctx == null)
                return;

            ctx.LastUpdateUtc = DateTime.UtcNow;
            _entries[positionId] = ctx;
        }

        public void RegisterEntry(EntryContext ctx)
        {
            if (ctx == null)
                return;

            ctx.LastUpdateUtc = DateTime.UtcNow;
        }

        public void RegisterPosition(PositionContext ctx)
        {
            if (ctx == null || ctx.PositionId <= 0)
                return;

            ctx.LastUpdateUtc = DateTime.UtcNow;
            _positions[ctx.PositionId] = ctx;
        }

        public PositionContext GetPosition(long id)
        {
            if (id <= 0)
                return null;

            _positions.TryGetValue(id, out var ctx);
            return ctx;
        }

        public EntryContext GetEntry(long id)
        {
            if (id <= 0)
                return null;

            _entries.TryGetValue(id, out var ctx);
            return ctx;
        }

        public void RemovePosition(long id)
        {
            if (id <= 0)
                return;

            _positions.Remove(id);
        }

        public void RemoveEntry(long id)
        {
            if (id <= 0)
                return;

            _entries.Remove(id);
        }

        public void PruneStale(TimeSpan maxAge, Action<long> onPruned = null)
        {
            DateTime now = DateTime.UtcNow;
            var stalePositions = new List<long>();
            var staleEntries = new List<long>();

            foreach (var kv in _positions)
            {
                if (now - kv.Value.LastUpdateUtc > maxAge)
                    stalePositions.Add(kv.Key);
            }

            foreach (var id in stalePositions)
            {
                _positions.Remove(id);
                onPruned?.Invoke(id);
            }

            foreach (var kv in _entries)
            {
                if (now - kv.Value.LastUpdateUtc > maxAge)
                    staleEntries.Add(kv.Key);
            }

            foreach (var id in staleEntries)
                _entries.Remove(id);
        }

        public void RebuildFromActivePositions(IEnumerable<Position> livePositions, Dictionary<long, PositionContext> source)
        {
            if (livePositions == null || source == null)
                return;

            var active = new HashSet<long>();
            foreach (var pos in livePositions)
            {
                if (pos == null || pos.Id <= 0)
                    continue;

                active.Add(pos.Id);

                if (source.TryGetValue(pos.Id, out var ctx) && ctx != null)
                {
                    ctx.LastUpdateUtc = DateTime.UtcNow;
                    _positions[pos.Id] = ctx;
                }
            }

            var stale = new List<long>();
            foreach (var id in _positions.Keys)
            {
                if (!active.Contains(id))
                    stale.Add(id);
            }

            foreach (var id in stale)
            {
                _positions.Remove(id);
                _entries.Remove(id);
            }
        }
    }
}
