using System;
using System.Collections.Generic;
using GeminiV26.Core;

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
            ["WEAK_STRUCTURE"] = 50,
            ["EARLY_PULLBACK"] = 53,
            ["PULLBACK_TOO_LONG"] = 49,
            ["PB_TOO_LONG"] = 49,
            ["PB_TOO_SHALLOW"] = 50,
            ["PB_TOO_DEEP"] = 49,
            ["ADX_TOO_LOW"] = 50,
            ["NO_TRIGGER"] = 51,
            ["FLAG_NOT_PERFECT"] = 52,
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

            if (!string.IsNullOrWhiteSpace(eval.Reason) &&
                eval.Reason.IndexOf("WAIT_BREAKOUT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                eval.TriggerConfirmed = false;
            }

            bool hardInvalid = IsHardInvalid(eval);
            if (!hardInvalid && eval.Direction != TradeDirection.None)
            {
                if (!eval.IsValid)
                {
                    eval.Score = Math.Max(eval.Score, ResolveSoftScore(eval.Reason));
                    eval.IsValid = true;
                }

                if (eval.Score >= MinScoreThreshold)
                    eval.IsValid = true;
            }
            else if (hardInvalid)
            {
                eval.IsValid = false;
            }

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



        public static EntryEvaluation SelectBalancedEvaluation(
            EntryContext ctx,
            EntryType type,
            EntryEvaluation longEval,
            EntryEvaluation shortEval)
        {
            ApplyGlobalCandidatePolicies(ctx, type, longEval);
            ApplyGlobalCandidatePolicies(ctx, type, shortEval);

            double longScore = Math.Max(0, longEval?.Score ?? 0);
            double shortScore = Math.Max(0, shortEval?.Score ?? 0);
            bool longHardInvalid = IsHardInvalid(longEval);
            bool shortHardInvalid = IsHardInvalid(shortEval);

            EntryEvaluation selectedEval;
            if (!longHardInvalid && !shortHardInvalid && Math.Abs(longScore - shortScore) < 1.0)
            {
                selectedEval = new EntryEvaluation
                {
                    Symbol = ctx?.Symbol,
                    Type = type,
                    Direction = TradeDirection.None,
                    Score = (int)Math.Round(Math.Max(longScore, shortScore)),
                    IsValid = false,
                    Reason = "SCORE_BALANCE_TIE"
                };
            }
            else if (longHardInvalid && shortHardInvalid)
            {
                selectedEval = longScore > shortScore ? longEval : shortEval;
            }
            else if (longHardInvalid)
            {
                selectedEval = shortEval;
            }
            else if (shortHardInvalid)
            {
                selectedEval = longEval;
            }
            else
            {
                selectedEval = longScore > shortScore ? longEval : shortEval;
            }

            var selectedDirection = selectedEval?.Direction ?? TradeDirection.None;
            string balanceLog = $"[SCORE BALANCE] type={type} longScore={longScore:0.##} shortScore={shortScore:0.##} selected={selectedDirection}";
            ctx?.Log?.Invoke(balanceLog);
            Console.WriteLine(balanceLog);

            return selectedEval;
        }

        private static void ApplyGlobalCandidatePolicies(EntryContext ctx, EntryType type, EntryEvaluation eval)
        {
            if (ctx == null || eval == null || eval.Direction == TradeDirection.None)
                return;

            int originalScore = eval.Score;
            int regimePenalty = 0;
            int directionPenalty = 0;
            int rangeBonus = 0;
            bool trendEntry = IsTrendBased(type);
            bool rangeEntry = IsRangeBased(type);
            bool confirmedBreakout = IsDirectionalBreakoutConfirmed(ctx, eval.Direction);
            bool strongImpulse = IsStrongDirectionalImpulse(ctx, eval.Direction);
            double minTrendAdx = Math.Max(ctx?.SessionMatrixConfig?.MinAdx ?? 0.0, 18.0);
            bool rangeDetected = ctx.IsRange_M5;
            bool noTrend = trendEntry &&
                           ctx.Adx_M5 < minTrendAdx &&
                           !strongImpulse &&
                           !confirmedBreakout;

            if (trendEntry && noTrend)
                regimePenalty += 25;

            if (ctx.Adx_M5 < 15.0 && rangeDetected)
            {
                if (trendEntry)
                    regimePenalty += 25;

                if (rangeEntry)
                    rangeBonus += 5;
            }

            eval.Score -= regimePenalty;
            eval.Score += rangeBonus;

            ctx.Log?.Invoke(
                $"[REGIME FILTER] type={type} dir={eval.Direction} adx={ctx.Adx_M5:F1} trendEntry={trendEntry} rangeEntry={rangeEntry} " +
                $"rangeDetected={rangeDetected} strongImpulse={strongImpulse} confirmedBreakout={confirmedBreakout} penalty={regimePenalty} bonus={rangeBonus} score={originalScore}->{eval.Score}");

            var (htfDirection, htfConfidence) = ResolveHtfBias(ctx);
            if (htfConfidence >= 0.60 && htfDirection != TradeDirection.None && eval.Direction != htfDirection)
                directionPenalty += 20;

            if (ctx.LogicBiasDirection != TradeDirection.None && ctx.LogicBiasConfidence >= 60 && eval.Direction != ctx.LogicBiasDirection)
                directionPenalty += 12;

            eval.Score -= directionPenalty;
            eval.Score = Math.Max(0, Math.Min(100, eval.Score));

            ctx.Log?.Invoke(
                $"[DIRECTION ALIGN] type={type} entry={eval.Direction} htf={htfDirection} htfConf={htfConfidence:0.00} " +
                $"logic={ctx.LogicBiasDirection} logicConf={ctx.LogicBiasConfidence} penalty={directionPenalty} score={originalScore}->{eval.Score}");

            bool hardViolation = IsHardInvalidReason(eval.Reason) || eval.Direction == TradeDirection.None;
            if (!hardViolation && eval.Score >= MinScoreThreshold)
                eval.IsValid = true;
            else if (hardViolation)
                eval.IsValid = false;

            ctx.Log?.Invoke(
                $"[VALID CHECK] type={type} dir={eval.Direction} score={eval.Score} hardViolation={hardViolation} finalValid={eval.IsValid}");
        }

        private static (TradeDirection direction, double confidence) ResolveHtfBias(EntryContext ctx)
        {
            if (ctx == null)
                return (TradeDirection.None, 0.0);

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx.Symbol);
            switch (instrumentClass)
            {
                case InstrumentClass.FX:
                    return (ctx.FxHtfAllowedDirection, ctx.FxHtfConfidence01);
                case InstrumentClass.CRYPTO:
                    return (ctx.CryptoHtfAllowedDirection, ctx.CryptoHtfConfidence01);
                case InstrumentClass.METAL:
                    return (ctx.MetalHtfAllowedDirection, ctx.MetalHtfConfidence01);
                case InstrumentClass.INDEX:
                    return (ctx.IndexHtfAllowedDirection, ctx.IndexHtfConfidence01);
                default:
                    return (TradeDirection.None, 0.0);
            }
        }

        private static bool IsTrendBased(EntryType type)
        {
            switch (type)
            {
                case EntryType.FX_Pullback:
                case EntryType.FX_Flag:
                case EntryType.FX_FlagContinuation:
                case EntryType.FX_MicroContinuation:
                case EntryType.FX_MicroStructure:
                case EntryType.FX_ImpulseContinuation:
                case EntryType.Index_Breakout:
                case EntryType.Index_Pullback:
                case EntryType.Index_Flag:
                case EntryType.XAU_Pullback:
                case EntryType.XAU_Impulse:
                case EntryType.XAU_Flag:
                case EntryType.Crypto_Impulse:
                case EntryType.Crypto_Flag:
                case EntryType.Crypto_Pullback:
                case EntryType.TC_Flag:
                case EntryType.TC_Pullback:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsRangeBased(EntryType type)
        {
            return type == EntryType.FX_RangeBreakout
                || type == EntryType.Crypto_RangeBreakout
                || type == EntryType.BR_RangeBreakout;
        }

        private static bool IsStrongDirectionalImpulse(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null)
                return false;

            if (direction == TradeDirection.Long)
                return ctx.HasImpulseLong_M5 && ctx.BarsSinceImpulseLong_M5 <= 2;

            if (direction == TradeDirection.Short)
                return ctx.HasImpulseShort_M5 && ctx.BarsSinceImpulseShort_M5 <= 2;

            return false;
        }

        private static bool IsDirectionalBreakoutConfirmed(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null)
                return false;

            if (direction == TradeDirection.Long)
            {
                return (ctx.HasBreakout_M1 && ctx.BreakoutDirection == TradeDirection.Long)
                    || ctx.FlagBreakoutUpConfirmed;
            }

            if (direction == TradeDirection.Short)
            {
                return (ctx.HasBreakout_M1 && ctx.BreakoutDirection == TradeDirection.Short)
                    || ctx.FlagBreakoutDownConfirmed;
            }

            return false;
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
