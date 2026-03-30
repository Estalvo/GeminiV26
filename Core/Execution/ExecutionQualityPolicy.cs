using System;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.Execution
{
    public enum ExecutionQualityTier
    {
        Full,
        Reduced,
        Salvage
    }

    public sealed class ExecutionQualityDecision
    {
        public ExecutionQualityTier Tier { get; init; }
        public double RiskMultiplier { get; init; }
        public bool ForceFastBE { get; init; }
        public bool ForceTightTrailing { get; init; }
        public string Reason { get; init; }
    }

    public static class ExecutionQualityPolicy
    {
        public static ExecutionQualityDecision Decide(
            EntryContext ctx,
            EntryEvaluation eval)
        {
            bool htfMismatch = false;
            TradeDirection allowedDirection = ctx?.ResolveAssetHtfAllowedDirection() ?? TradeDirection.None;

            if (ctx != null &&
                allowedDirection != TradeDirection.None)
            {
                htfMismatch = ctx.FinalDirection != allowedDirection;
            }

            bool lowScore = eval != null && eval.Score < 50;

            var decision = new ExecutionQualityDecision
            {
                Tier = ExecutionQualityTier.Full,
                RiskMultiplier = 1.0,
                ForceFastBE = false,
                ForceTightTrailing = false,
                Reason = "FULL_QUALITY"
            };

            if (htfMismatch && lowScore)
            {
                double instrumentMultiplier = GetInstrumentMultiplier(ctx?.SymbolName);

                decision = new ExecutionQualityDecision
                {
                    Tier = ExecutionQualityTier.Reduced,
                    RiskMultiplier = instrumentMultiplier,
                    ForceFastBE = true,
                    ForceTightTrailing = true,
                    Reason = "HTF_MISMATCH_LOW_SCORE"
                };
            }

            ctx?.Log?.Invoke($"[EXEC_QUALITY] tier={decision.Tier} mult={decision.RiskMultiplier} reason={decision.Reason}");

            return decision;
        }

        private static double GetInstrumentMultiplier(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return 0.5;

            if (Contains(symbol, "US30") || Contains(symbol, "US TECH") || Contains(symbol, "NAS"))
                return 0.4;

            if (Contains(symbol, "XAU"))
                return 0.3;

            if (Contains(symbol, "USD") || Contains(symbol, "EUR") || Contains(symbol, "JPY"))
                return 0.5;

            if (Contains(symbol, "BTC") || Contains(symbol, "ETH"))
                return 0.35;

            return 0.5;
        }

        private static bool Contains(string value, string token)
        {
            return value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
