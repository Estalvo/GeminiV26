using System;
using System.Collections.Generic;

namespace GeminiV26.Core.Matrix
{
    public static class SessionMatrixDefaults
    {
        public static SessionMatrixConfig Neutral => new SessionMatrixConfig
        {
            AllowFlag = true,
            AllowPullback = true,
            AllowBreakout = true,
            MinAtrMultiplier = 1.0,
            MinAdx = 0.0,
            MinEmaDistance = 0.0,
            EntryScoreModifier = 0.0
        };

        public static IReadOnlyDictionary<string, IReadOnlyDictionary<SessionBucket, SessionMatrixConfig>> Build()
        {
            return new Dictionary<string, IReadOnlyDictionary<SessionBucket, SessionMatrixConfig>>(StringComparer.OrdinalIgnoreCase)
            {
                ["FX"] = Fx(),
                ["INDEX"] = Index(),
                ["METAL"] = Metal(),
                ["CRYPTO"] = Crypto()
            };
        }

        private static IReadOnlyDictionary<SessionBucket, SessionMatrixConfig> Fx() => new Dictionary<SessionBucket, SessionMatrixConfig>
        {
            [SessionBucket.Asia] = new SessionMatrixConfig { AllowFlag = false, AllowPullback = true, AllowBreakout = false, MinAtrMultiplier = 1.2, MinAdx = 22, MinEmaDistance = 0.5, EntryScoreModifier = 0 },
            [SessionBucket.London] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 },
            [SessionBucket.LondonNyOverlap] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 2 },
            [SessionBucket.NewYork] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 1 }
        };

        private static IReadOnlyDictionary<SessionBucket, SessionMatrixConfig> Index() => new Dictionary<SessionBucket, SessionMatrixConfig>
        {
            [SessionBucket.Asia] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = false, AllowBreakout = false, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 },
            [SessionBucket.London] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 },
            [SessionBucket.LondonNyOverlap] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 2 },
            [SessionBucket.NewYork] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 }
        };

        private static IReadOnlyDictionary<SessionBucket, SessionMatrixConfig> Metal() => new Dictionary<SessionBucket, SessionMatrixConfig>
        {
            [SessionBucket.Asia] = new SessionMatrixConfig { AllowFlag = false, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 },
            [SessionBucket.London] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 },
            [SessionBucket.LondonNyOverlap] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 2 },
            [SessionBucket.NewYork] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 18, MinEmaDistance = 0.0, EntryScoreModifier = 0 }
        };

        private static IReadOnlyDictionary<SessionBucket, SessionMatrixConfig> Crypto() => new Dictionary<SessionBucket, SessionMatrixConfig>
        {
            [SessionBucket.CryptoAlwaysOn] = new SessionMatrixConfig { AllowFlag = true, AllowPullback = true, AllowBreakout = true, MinAtrMultiplier = 1.0, MinAdx = 0.0, MinEmaDistance = 0.0, EntryScoreModifier = 0 }
        };
    }
}
