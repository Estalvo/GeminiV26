using System;
using System.Collections.Generic;

namespace GeminiV26.Core.Matrix
{
    public sealed class SessionMatrixProvider
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<SessionBucket, SessionMatrixConfig>> _map;

        public SessionMatrixProvider()
        {
            _map = SessionMatrixDefaults.Build();
        }

        public SessionMatrixConfig GetConfig(string instrumentClass, SessionBucket bucket, TimeframeTier tier)
        {
            var baseCfg = SessionMatrixDefaults.Neutral;

            if (!_map.TryGetValue(instrumentClass ?? string.Empty, out var bySession))
                return baseCfg;

            if (!bySession.TryGetValue(bucket, out var cfg))
                return baseCfg;

            var cloned = Clone(cfg);

            if (tier == TimeframeTier.Scalping)
            {
                cloned.MinAdx += 1;
            }
            else if (tier == TimeframeTier.BroadIntraday)
            {
                cloned.MinAtrMultiplier = Math.Max(1.0, cloned.MinAtrMultiplier - 0.1);
            }

            return cloned;
        }

        private static SessionMatrixConfig Clone(SessionMatrixConfig cfg)
        {
            return new SessionMatrixConfig
            {
                AllowFlag = cfg.AllowFlag,
                AllowPullback = cfg.AllowPullback,
                AllowBreakout = cfg.AllowBreakout,
                MinAtrMultiplier = cfg.MinAtrMultiplier,
                MinAdx = cfg.MinAdx,
                MinEmaDistance = cfg.MinEmaDistance,
                EntryScoreModifier = cfg.EntryScoreModifier
            };
        }
    }
}
