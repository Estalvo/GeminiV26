// =========================================================
// GEMINI V26 – TC_PullbackEntry
// Rulebook 1.0 compliant EntryType
// =========================================================

using System;
using GeminiV26.Core.Entry;
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
        private const int MIN_SCORE = 50;
        public double Atr_M5 { get; set; }

        // =========================================================
        // CSV AUDIT – MULTI INSTANCE SAFE (NON-BLOCKING)
        // =========================================================
        private static readonly object _csvLock = new object();

        public EntryEvaluation Evaluate(EntryContext ctx)
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

            int score = 0;

            // =========================================================
            // M5 candle (kanóc) – HELYES API
            // =========================================================
            var bars = ctx.M5;
            
            // 🔒 BIZTONSÁGI GUARD – IDE JÖN
            if (bars == null || bars.Count < 2)
            {
                eval.Reason += "NoM5Bars;";
                return eval;
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

            if (ctx.Ema21Slope_M15 > 0 && ctx.Ema21Slope_M5 > 0)
            {
                eval.Direction = TradeDirection.Long;
                score += strongTrend ? 30 : 20;
            }
            else if (ctx.Ema21Slope_M15 < 0 && ctx.Ema21Slope_M5 < 0)
            {
                eval.Direction = TradeDirection.Short;
                score += strongTrend ? 30 : 20;
            }
            else eval.Reason += "WeakTrend;";

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
            if (ctx.Symbol.Contains("XAU"))
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
                    return eval;
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
                    return eval;
                }

                // =====================================================
                // 4️⃣ KIFULLADÁS / TOP-CHASE SZŰRÉS (KANÓC)
                // =====================================================
                if (wickRatio < 0.45)
                {
                    eval.Reason += "XAU_WickExhaustion;";
                    return eval;
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
                    return eval;
                }

                if (
                    eval.Direction == TradeDirection.Short &&
                    ctx.Ema8_M5 >= ctx.Ema21_M5
                )
                {
                    eval.Reason += "XAU_ShortTrendBroken;";
                    return eval;
                }

                // ✔️ Ha idáig eljutott → PROFITABLE XAU SETUP
            }

            // =========================================================
            // 🛑 NAS / US30 – INDEX STRICT (API-safe)
            // =========================================================
            if (
                ctx.Symbol.Contains("US TECH") ||
                ctx.Symbol.Contains("NAS") ||
                ctx.Symbol.Contains("US30") ||
                ctx.Symbol.Contains("US 30")
            )
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
                    return eval;
                }
            }

            if (ctx.Symbol.Contains("EUR"))
            {
                // Range-ben nem kereskedünk
                if (ctx.IsRange_M5)
                {
                    eval.Reason += "EUR_RangeBlocked;";
                    return eval;
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
            if (ctx.Symbol.Contains("GBP"))
            {
                if (ctx.IsRange_M5)
                {
                    eval.Reason += "GBP_RangeBlocked;";
                    return eval;
                }

                if (wickRatio < 0.45)
                {
                    eval.Reason += "GBP_WickDominance;";
                    return eval;
                }
            }

            // =========================================================
            // 🛑 USDJPY – SNAPBACK
            // =========================================================
            if (ctx.Symbol.Contains("JPY"))
            {
                if (ctx.IsRange_M5)
                {
                    eval.Reason += "JPY_RangeBlocked;";
                    return eval;
                }

                if (wickRatio < 0.40 && ctx.HasImpulse_M5)
                {
                    eval.Reason += "JPY_WickRejection;";
                    return eval;
                }
            }

            // =========================================================
            // EXTRA QUALITY
            // =========================================================
            if (ctx.HasImpulse_M5) score += 5;
            if (ctx.IsAtrExpanding_M5) score += 5;

            // =========================================================
            // FINAL RULES
            // =========================================================
            if (eval.Direction == TradeDirection.None)
            {
                eval.Reason += "NoDirection;";
                return eval;
            }

            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            // =========================================================
            // CSV AUDIT LOG (NON-BLOCKING, TRADE-SAFE)
            // =========================================================
            WriteAuditCsvSafe(ctx, eval, wickRatio);

            return eval;
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
    }
}
