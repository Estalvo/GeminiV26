using System;
using cAlgo.API;

namespace GeminiV26.Core.TradeManagement
{
    public sealed class StructurePoint
    {
        public int Index { get; init; }
        public DateTime Time { get; init; }
        public double Price { get; init; }
    }

    public sealed class StructureSnapshot
    {
        public StructurePoint LastSwingLow { get; init; }
        public StructurePoint LastSwingHigh { get; init; }
        public StructurePoint LastHigherLow { get; init; }
        public StructurePoint LastLowerHigh { get; init; }
    }

    public sealed class StructureTracker
    {
        private readonly Robot _bot;
        private readonly Bars _bars;
        private readonly int _strength;

        private DateTime _lastProcessedBarTime = DateTime.MinValue;
        private StructurePoint _lastSwingLow;
        private StructurePoint _lastSwingHigh;
        private StructurePoint _lastHigherLow;
        private StructurePoint _lastLowerHigh;

        public StructureTracker(Robot bot, Bars bars, int strength = 2)
        {
            _bot = bot;
            _bars = bars;
            _strength = Math.Max(2, strength);
        }

        public StructureSnapshot GetSnapshot()
        {
            UpdateStructureIfNeeded();
            return new StructureSnapshot
            {
                LastSwingLow = _lastSwingLow,
                LastSwingHigh = _lastSwingHigh,
                LastHigherLow = _lastHigherLow,
                LastLowerHigh = _lastLowerHigh
            };
        }

        public bool TryGetLastHigherLow(out StructurePoint point)
        {
            UpdateStructureIfNeeded();
            point = _lastHigherLow;
            return point != null;
        }

        public bool TryGetLastLowerHigh(out StructurePoint point)
        {
            UpdateStructureIfNeeded();
            point = _lastLowerHigh;
            return point != null;
        }

        public StructurePoint GetLastConfirmedSwingLow()
        {
            UpdateStructureIfNeeded();
            return _lastSwingLow;
        }

        public StructurePoint GetLastConfirmedSwingHigh()
        {
            UpdateStructureIfNeeded();
            return _lastSwingHigh;
        }

        private void UpdateStructureIfNeeded()
        {
            if (_bars.Count < (_strength * 2 + 3))
                return;

            // csak új bar esetén frissítünk (OnTick-safe)
            DateTime currentBarTime = _bars.OpenTimes.LastValue;
            if (currentBarTime == _lastProcessedBarTime)
                return;

            _lastProcessedBarTime = currentBarTime;

            int lastClosed = _bars.Count - 2;
            int pivotIndex = lastClosed - _strength;
            if (pivotIndex <= _strength)
                return;

            bool isSwingLow = true;
            bool isSwingHigh = true;

            double pivotLow = _bars.LowPrices[pivotIndex];
            double pivotHigh = _bars.HighPrices[pivotIndex];

            for (int i = pivotIndex - _strength; i <= pivotIndex + _strength; i++)
            {
                if (i == pivotIndex)
                    continue;

                if (_bars.LowPrices[i] <= pivotLow)
                    isSwingLow = false;

                if (_bars.HighPrices[i] >= pivotHigh)
                    isSwingHigh = false;

                if (!isSwingLow && !isSwingHigh)
                    break;
            }

            if (isSwingLow)
            {
                var newLow = new StructurePoint
                {
                    Index = pivotIndex,
                    Time = _bars.OpenTimes[pivotIndex],
                    Price = pivotLow
                };

                if (_lastSwingLow == null || newLow.Price > _lastSwingLow.Price)
                    _lastHigherLow = newLow;

                _lastSwingLow = newLow;
                _bot.Print($"[STRUCT] newSwingLow t={newLow.Time:O} price={newLow.Price}");
            }

            if (isSwingHigh)
            {
                var newHigh = new StructurePoint
                {
                    Index = pivotIndex,
                    Time = _bars.OpenTimes[pivotIndex],
                    Price = pivotHigh
                };

                if (_lastSwingHigh == null || newHigh.Price < _lastSwingHigh.Price)
                    _lastLowerHigh = newHigh;

                _lastSwingHigh = newHigh;
                _bot.Print($"[STRUCT] newSwingHigh t={newHigh.Time:O} price={newHigh.Price}");
            }
        }
    }
}
