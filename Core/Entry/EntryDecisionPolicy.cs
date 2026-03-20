using System;
using System.Collections.Generic;

namespace GeminiV26.Core.Entry
{
    public static class EntryDecisionPolicy
    {
        public const int MinScoreThreshold = 55;

        private static readonly string[] HardSafetyReasonTokens =
        {
            "CTX_NOT_READY",
            "ATR_NOT_READY",
            "ATR_ZERO",
            "NO_PROFILE",
            "NO_FX_PROFILE",
            "NO_INDEX_PROFILE",
            "NO_CRYPTO_PROFILE",
            "NO_TUNING",
            "NO_FLAG_TUNING",
            "NO_SESSION",
            "NO_SESSION_TUNING",
            "NO_MARKETSTATE",
            "NO_TREND_STATE",
            "NO_TREND_DIR",
            "NO_CLEAR_DIRECTION",
            "NO_DIRECTION",
            "FLAG_DIRECTION_INVALID",
            "SESSION_DISABLED",
            "SESSION_MATRIX_ALLOW",
            "SESSION_MATRIX_BREAKOUT_DISABLED",
            "SESSION_MATRIX_PULLBACK_DISABLED",
            "SESSION_MATRIX_ATR_AVG_UNAVAILABLE",
            "SESSION_MATRIX_EMA_DISTANCE_TOO_LOW",
            "NO_RANGE",
            "NOT_RANGE",
            "AMBIGUOUS_SPIKE",
            "PB_TOO_DEEP_HARD",
            "PB_TOO_DEEP_EXTREME",
            "PULLBACK_TOO_DEEP_EXTREME",
            "DATA",
            "INVALID_STATE",
            "INSUFFICIENT",
            "NO_VALID_SIDE"
        };

        private static readonly Dictionary<string, int> SoftReasonScores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["LOW_SCORE"] = 50,
            ["SCORE_TOO_LOW"] = 50,
            ["NO_M1"] = 51,
            ["NO_M1_TRIGGER"] = 51,
            ["NO_M1_CONFIRM"] = 51,
            ["NO_M1_CONFIRMATION"] = 51,
            ["NO_REACTION"] = 50,
            ["WEAK"] = 50,
            ["EARLY_PULLBACK"] = 53,
            ["PULLBACK_TOO_LONG"] = 49,
            ["PB_TOO_LONG"] = 49,
            ["PB_TOO_SHALLOW"] = 50,
            ["PB_TOO_DEEP"] = 49,
            ["STALE_IMPULSE"] = 50,
            ["IMPULSE_TOO_OLD"] = 50,
            ["NO_PULLBACK"] = 50,
            ["NO_DECELERATION"] = 51,
            ["NO_CONTINUATION_SIGNAL"] = 50,
            ["WAIT_BREAKOUT"] = 52,
            ["NO_FLAG"] = 50,
            ["NO_FLAG_RANGE"] = 49,
            ["NO_FLAG_STRUCTURE"] = 50,
            ["NO_IMPULSE"] = 50,
            ["NO_BREAK"] = 51,
            ["NO_BREAKOUT"] = 51,
            ["TRANSITION"] = 52,
            ["HTF"] = 52,
            ["IN_RANGE"] = 0,
            ["NO_TREND"] = 0,
            ["RANGE_BUT_ADX_STRONG"] = 0,
            ["FX_REV_ASIA_BLOCKED"] = 0,
            ["DISABLED"] = 0
        };

        public static EntryEvaluation Normalize(EntryEvaluation eval)
        {
            if (eval == null)
                return null;

            eval.Score = Math.Max(0, Math.Min(100, eval.Score));
            eval.LogicConfidence = PositionContext.ClampRiskConfidence(eval.LogicConfidence);
            eval.State = ResolveState(eval);
            return eval;
        }

        public static bool IsHardInvalid(EntryEvaluation eval)
        {
            if (eval == null)
                return true;

            if (eval.IsValid)
                return false;

            return IsHardInvalidReason(eval.Reason) || eval.Direction == TradeDirection.None;
        }

        public static bool IsHardInvalidReason(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return false;

            foreach (var token in HardSafetyReasonTokens)
            {
                if (reason.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        public static bool Accepts(EntryEvaluation eval)
        {
            return eval != null && eval.IsValid && eval.Score >= MinScoreThreshold;
        }

        public static int ResolveSoftScore(string reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return MinScoreThreshold - 5;

            foreach (var kvp in SoftReasonScores)
            {
                if (reason.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kvp.Value;
            }

            return MinScoreThreshold - 5;
        }

        private static EntryState ResolveState(EntryEvaluation eval)
        {
            if (eval == null)
                return EntryState.NONE;

            if (eval.TriggerConfirmed && eval.Score >= MinScoreThreshold && eval.Direction != TradeDirection.None)
                return EntryState.TRIGGERED;

            if (eval.Score >= MinScoreThreshold && eval.Direction != TradeDirection.None && !IsHardInvalid(eval))
                return EntryState.ARMED;

            if (eval.Score > 0 && eval.Direction != TradeDirection.None && !IsHardInvalid(eval))
                return EntryState.SETUP_DETECTED;

            return EntryState.NONE;
        }
    }
}
