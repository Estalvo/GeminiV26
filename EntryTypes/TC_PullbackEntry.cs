// =========================================================
// GEMINI V26 – TC_PullbackEntry
// Rulebook 1.0 compliant EntryType
// =========================================================

using System;
using GeminiV26.Core.Entry;
using GeminiV26.Core;
using System.IO;
using System.Text;
using System.Globalization;

namespace GeminiV26.EntryTypes
{
    public class TC_PullbackEntry : IEntryType
    {
        public EntryType Type => EntryType.TC_Pullback;

        private const double MinSlope = 0.0005;
        private const double MaxPullbackATR = 1.2;
        private const int MIN_SCORE = EntryDecisionPolicy.MinScoreThreshold;
        public double Atr_M5 { get; set; }

        // =========================================================
        // CSV AUDIT – MULTI INSTANCE SAFE (NON-BLOCKING)
        // =========================================================
        private static readonly object _csvLock = new object();

        public EntryEvaluation Evaluate(EntryContext ctx)
        {
            DirectionDebug.LogOnce(ctx);
            if (ctx == null || !ctx.IsReady)
            {
                return CreateInvalid(ctx, "CTX_NOT_READY;");
            }

            if (ctx.LogicBias == TradeDirection.None)
            {
                return CreateInvalid(ctx, "NO_LOGIC_BIAS");
            }

            if (ctx.HtfConfidence >= 0.6 && ctx.HtfDirection != ctx.LogicBias)
            {
                return CreateInvalid(ctx, "HTF_MISMATCH");
            }

            if (ctx.LogicBias == TradeDirection.Long)
            {
                var eval = EvaluateDirectional(ctx, TradeDirection.Long);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), eval, null, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }
            else if (ctx.LogicBias == TradeDirection.Short)
            {
                var eval = EvaluateDirectional(ctx, TradeDirection.Short);
                EntryDirectionQuality.LogDecision(ctx, Type.ToString(), null, eval, eval.Direction);
                return EntryDecisionPolicy.Normalize(eval);
            }

            return CreateInvalid(ctx, "NO_LOGIC_BIAS");
        }
        private EntryEvaluation EvaluateDirectional(EntryContext ctx, TradeDirection forcedDirection)
        {
            var eval = new EntryEvaluation
            {
                Symbol = ctx.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = 0,
                IsValid = false,
                Reason = ""
            };
            EntryEvaluation FinalizeEval(bool applyTrendRegimePenalty = true)
            {
                eval.Score = ApplyMandatoryEntryAdjustments(ctx, eval.Direction, eval.Score, applyTrendRegimePenalty);
                return eval;
            }

            int score = 0;
            int setupScore = 0;

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(ctx.Symbol);
            string symbolCanonical = SymbolRouting.NormalizeSymbol(ctx.Symbol);

            // =========================================================
            // M5 candle (kanóc) – HELYES API
            // =========================================================
            var bars = ctx.M5;
            
            // 🔒 BIZTONSÁGI GUARD – IDE JÖN
            if (bars == null || bars.Count < 2)
            {
                eval.Reason += "NoM5Bars;";
                return FinalizeEval();
            }

            int i = bars.Count - 1;

            double high = bars.HighPrices[i];
            double low = bars.LowPrices[i];
            double open = bars.OpenPrices[i];
            double close = bars.ClosePrices[i];

            double range = high - low;
            double body = Math.Abs(close - open);
            double wickRatio = range > 0 ? body / range : 1.0;

            // =========================================================
            // 1️⃣ KÖRNYEZET
            // =========================================================
            if (!ctx.IsRange_M5) score += 15;
            else eval.Reason += "RangeEnv;";

            // =========================================================
            // 2️⃣ TREND
            // =========================================================
            bool strongTrend =
                Math.Abs(ctx.Ema21Slope_M15) > MinSlope * 1.5 &&
                Math.Abs(ctx.Ema21Slope_M5) > MinSlope;

            bool longTrend = ctx.Ema21Slope_M15 > 0 && ctx.Ema21Slope_M5 > 0;
            bool shortTrend = ctx.Ema21Slope_M15 < 0 && ctx.Ema21Slope_M5 < 0;
            bool dirTrendOk =
                forcedDirection == TradeDirection.Long ? longTrend :
                forcedDirection == TradeDirection.Short ? shortTrend :
                false;

            eval.Direction = forcedDirection;

            if (dirTrendOk)
            {
                score += strongTrend ? 30 : 20;
            }
            else
            {
                score -= 20;
                eval.Reason += "WeakTrend;";
            }

            // =========================================================
            // 3️⃣ PULLBACK
            // =========================================================
            bool softNoPullback = !ctx.PullbackTouchedEma21_M5;
            // ---------------------------------------------------------
            // 🔁 EMA ZONE PULLBACK (EMA8–EMA21 közé visszahúzás)
            // ---------------------------------------------------------
            bool emaZonePullback =
                ctx.Ema8_M5 > ctx.Ema21_M5 &&
                (ctx.Ema8_M5 - ctx.Ema21_M5) < (ctx.Ema21_M5 * 0.001);

            bool noM1Trigger = !ctx.M1TriggerInTrendDirection;

            if (!softNoPullback) score += 15;
            else eval.Reason += "SoftNoPullback;";

            if (ctx.PullbackDepthAtr_M5 <= MaxPullbackATR) score += 10;
            else eval.Reason += "PullbackTooDeep;";

            // =========================================================
            // 4️⃣ TRIGGER
            // =========================================================
            if (!noM1Trigger) score += 10;
            else eval.Reason += "NoM1Trigger;";

            // =========================================================
            // 🛑 XAU – PROFIT-ORIENTED HARD CONTROL
            // =========================================================
            if (instrumentClass == InstrumentClass.METAL)
            {
                // =====================================================
                // 1️⃣ LOW ENERGY / KIFULLADÁS – HARD TILT
                // =====================================================
                bool emaCompressed =
                    Math.Abs(ctx.Ema8_M5 - ctx.Ema21_M5) <
                    (ctx.Ema21_M5 * 0.00025);

                if (!ctx.IsAtrExpanding_M5 && emaCompressed)
                {
                    eval.Reason += "XAU_LowEnergy_Block;";
                    return FinalizeEval();
                }

                // 2️⃣ SOFT ENTRY – SCORE BASED (NEM HARD RETURN)
                if (softNoPullback)
                {
                    score -= 25;
                    eval.Reason += "XAU_SoftNoPullback;";
                }

                if (noM1Trigger)
                {
                    score -= 20;
                    eval.Reason += "XAU_NoM1Trigger;";
                }

                // =====================================================
                // 3️⃣ PULLBACK MINŐSÉG (EMA21 KÖZELI VISSZAHÚZÁS)
                // =====================================================
                bool validPullback =
                    ctx.PullbackTouchedEma21_M5 &&
                    ctx.PullbackDepthAtr_M5 <= 1.0;

                if (!validPullback)
                {
                    eval.Reason += "XAU_PullbackInvalid;";
                    return FinalizeEval();
                }

                // =====================================================
                // 4️⃣ KIFULLADÁS / TOP-CHASE SZŰRÉS (KANÓC)
                // =====================================================
                if (wickRatio < 0.45)
                {
                    eval.Reason += "XAU_WickExhaustion;";
                    return FinalizeEval();
                }

                // =====================================================
                // 5️⃣ TREND IRÁNY MEGERŐSÍTÉS
                // =====================================================
                if (
                    eval.Direction == TradeDirection.Long &&
                    ctx.Ema8_M5 <= ctx.Ema21_M5
                )
                {
                    eval.Reason += "XAU_LongTrendBroken;";
                    return FinalizeEval();
                }

                if (
                    eval.Direction == TradeDirection.Short &&
                    ctx.Ema8_M5 >= ctx.Ema21_M5
                )
                {
                    eval.Reason += "XAU_ShortTrendBroken;";
                    return FinalizeEval();
                }

                // ✔️ Ha idáig eljutott → PROFITABLE XAU SETUP
            }

            // =========================================================
            // 🛑 NAS / US30 – INDEX STRICT (API-safe)
            // =========================================================
            if (instrumentClass == InstrumentClass.INDEX)
            {
                // EMA zone pullback feloldja a softNoPullback-et indexeken
                if (softNoPullback && emaZonePullback)
                {
                    softNoPullback = false;
                    eval.Reason += "IDX_EMAZonePullback;";
                }

                // ❌ Soft entry tiltás indexeken
                if (softNoPullback && noM1Trigger)
                {
                    score -= 30;
                    eval.Reason += "IDX_NoSoftEntry;";
                }

                // =====================================================
                // NAS – KÉSŐI IMPULZUS KIFULLADÁS (ATR-alapú proxy)
                //
                // Mivel NINCS ImpulseBarsAgo:
                // - volt impulzus
                // - EMA-k már közel vannak egymáshoz
                // - ATR nem bővül
                // => impulzus kifulladt
                // =====================================================
                bool emaConverging =
                    ctx.AtrM5 > 0 &&
                    Math.Abs(ctx.Ema8_M5 - ctx.Ema21_M5) < ctx.AtrM5 * 0.15;

                if (
                    ctx.HasImpulse_M5 &&
                    emaConverging &&
                    !ctx.IsAtrExpanding_M5
                )
                {
                    eval.Reason += "IDX_LateImpulseFade;";
                    return FinalizeEval();
                }
            }

            if (symbolCanonical == "EURUSD")
            {
                // Range-ben nem kereskedünk
                if (ctx.IsRange_M5)
                {
                    eval.Reason += "EUR_RangeBlocked;";
                    return FinalizeEval();
                }

                // EMA zone pullback elfogadása EURUSD-n (nem hard return)
                if (softNoPullback && emaZonePullback)
                {
                    softNoPullback = false;
                    eval.Reason += "EUR_EMAZonePullback;";
                }

                // Soft pullback büntetés EURUSD-n (nem hard return)
                if (softNoPullback)
                {
                    score -= 20;
                    eval.Reason += "EUR_SoftPenalty;";
                }

                // M1 trigger ENYHÍTVE:
                // elfogadjuk, ha volt M5 impulzus
                if (noM1Trigger && !ctx.HasImpulse_M5)
                {
                    score -= 25;
                    eval.Reason += "EUR_NoTriggerOrImpulse;";
                }
            }

            // =========================================================
            // 🛑 GBPUSD – STOPHUNT PROXY
            // =========================================================
            if (symbolCanonical == "GBPUSD")
            {
                if (ctx.IsRange_M5)
                {
                    eval.Reason += "GBP_RangeBlocked;";
                    return FinalizeEval();
                }

                if (wickRatio < 0.45)
                {
                    eval.Reason += "GBP_WickDominance;";
                    return FinalizeEval();
                }
            }

            // =========================================================
            // 🛑 USDJPY – SNAPBACK
            // =========================================================
            if (symbolCanonical == "USDJPY" || symbolCanonical == "EURJPY" || symbolCanonical == "GBPJPY")
            {
                if (ctx.IsRange_M5)
                {
                    eval.Reason += "JPY_RangeBlocked;";
                    return FinalizeEval();
                }

                if (wickRatio < 0.40 && ctx.HasImpulse_M5)
                {
                    eval.Reason += "JPY_WickRejection;";
                    return FinalizeEval();
                }
            }

            // =========================================================
            // EXTRA QUALITY
            // =========================================================
            if (ctx.HasImpulse_M5) score += 5;
            if (ctx.IsAtrExpanding_M5) score += 5;

            if (instrumentClass == InstrumentClass.METAL)
            {
                bool hasStructure =
                    (eval.Direction == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5)
                    || (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5)
                    || ctx.HasEarlyPullback_M5;

                if (!hasStructure)
                    setupScore -= 40;
                else
                    setupScore += 20;

                bool hasConfirmation =
                    (!noM1Trigger) || ctx.LastClosedBarInTrendDirection;

                if (hasConfirmation)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.INDEX)
            {
                bool hasImpulse = ctx.HasImpulse_M5;
                if (!hasImpulse)
                    setupScore -= 40;
                else
                    setupScore += 15;

                bool hasStructure =
                    ctx.HasPullbackLong_M5 || ctx.HasPullbackShort_M5;

                if (hasStructure)
                    setupScore += 10;

                bool continuationSignal = !noM1Trigger;
                bool breakoutConfirmed = continuationSignal;

                if (continuationSignal || breakoutConfirmed)
                    setupScore += 20;
            }
            else if (instrumentClass == InstrumentClass.CRYPTO)
            {
                if (!ctx.IsAtrExpanding_M5)
                    setupScore -= 30;

                bool hasStructure =
                    (eval.Direction == TradeDirection.Long ? ctx.HasFlagLong_M5 : ctx.HasFlagShort_M5)
                    || (ctx.PullbackBars_M5 >= 2 && ctx.IsPullbackDecelerating_M5);

                if (!hasStructure)
                    setupScore -= 30;
                else
                    setupScore += 15;

                bool continuationSignal = !noM1Trigger;

                if (continuationSignal)
                    setupScore += 20;
            }
            else
            {
                double pullbackDepthR =
                    eval.Direction == TradeDirection.Short
                        ? ctx.PullbackDepthRShort_M5
                        : ctx.PullbackDepthRLong_M5;

                bool hasStructure =
                    pullbackDepthR >= 0.15;

                if (!hasStructure)
                    setupScore -= 35;
                else
                    setupScore += 15;

                bool continuationSignal = !noM1Trigger;

                if (continuationSignal)
                    setupScore += 20;
            }

            // =========================================================
            // FINAL RULES
            // =========================================================
            bool breakoutDetected =
                !noM1Trigger ||
                (ctx.HasBreakout_M1 && ctx.BreakoutDirection == eval.Direction);
            bool strongCandle = ctx.LastClosedBarInTrendDirection;
            bool followThrough = breakoutDetected || ctx.HasReactionCandle_M5;
            score = TriggerScoreModel.Apply(ctx, $"TC_PULLBACK_{eval.Direction}", score, breakoutDetected, strongCandle, followThrough, "NO_PULLBACK_TRIGGER");
            score += setupScore;

            if (setupScore <= 0)
                score = Math.Min(score, MIN_SCORE - 10);

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            // =========================================================
            // CSV AUDIT LOG (NON-BLOCKING, TRADE-SAFE)
            // =========================================================
            WriteAuditCsvSafe(ctx, eval, wickRatio);

            return FinalizeEval();
        }

        // =========================================================
        // CSV AUDIT WRITER – MULTI CTRADER SAFE
        // =========================================================
        private void WriteAuditCsvSafe(
            EntryContext ctx,
            EntryEvaluation eval,
            double wickRatio
        )
        {
            try
            {
                string baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "GeminiV26",
                    "EntryAudit",
                    ctx.Symbol
                );

                Directory.CreateDirectory(baseDir);

                string filePath = Path.Combine(
                    baseDir,
                    $"TC_Pullback_{DateTime.UtcNow:yyyyMMdd}.csv"
                );

                bool writeHeader = !File.Exists(filePath);

                var sb = new StringBuilder();

                if (writeHeader)
                {
                    sb.AppendLine(
                        "Time,Symbol,Direction,Score,IsValid," +
                        "SoftNoPullback,NoM1Trigger,PullbackATR,WickRatio," +
                        "HasImpulse,ATRExpanding,Reason"
                    );
                }

                sb.AppendLine(string.Join(",",
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    ctx.Symbol,
                    eval.Direction,
                    eval.Score,
                    eval.IsValid,
                    !ctx.PullbackTouchedEma21_M5,
                    !ctx.M1TriggerInTrendDirection,
                    ctx.PullbackDepthAtr_M5.ToString(CultureInfo.InvariantCulture),
                    wickRatio.ToString(CultureInfo.InvariantCulture),
                    ctx.HasImpulse_M5,
                    ctx.IsAtrExpanding_M5,
                    eval.Reason.Replace(",", "|")
                ));

                lock (_csvLock)
                {
                    using (var fs = new FileStream(
                        filePath,
                        FileMode.Append,
                        FileAccess.Write,
                        FileShare.ReadWrite))
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.Write(sb.ToString());
                    }
                }
            }
            catch
            {
                // ❗ SOHA nem akadályozhatja a trade-et
            }
        }

        private static int ApplyMandatoryEntryAdjustments(EntryContext ctx, TradeDirection direction, int score, bool applyTrendRegimePenalty)
        {
            return EntryDirectionQuality.Apply(
                ctx,
                direction,
                score,
                new DirectionQualityRequest
                {
                    TypeTag = "TC_PullbackEntry",
                    ApplyTrendRegimePenalty = applyTrendRegimePenalty
                });
        }

        private EntryEvaluation CreateInvalid(EntryContext ctx, string reason)
        {
            return new EntryEvaluation
            {
                Symbol = ctx?.Symbol,
                Type = Type,
                Direction = TradeDirection.None,
                Score = ApplyMandatoryEntryAdjustments(ctx, TradeDirection.None, 0, true),
                IsValid = false,
                Reason = reason
            };
        }

    }
}
