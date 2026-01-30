// =========================================================
// GEMINI V26 – TC_FlagEntry (STRICT FLAG VERSION)
// Rulebook 1.0 compliant EntryType
//
// Alapelv:
// - Flag setup csak VALÓDI M5 impulse UTÁN értelmezhető
// - Flag struktúra HARD feltétel
// - Trend + impulse irány dönti el az irányt
// - M1 breakout trigger HARD belépési pecsét
// - Score = minőség, nem létezési feltétel
// =========================================================

using GeminiV26.Core.Entry;
using System;

namespace GeminiV26.EntryTypes
{
    public class TC_FlagEntry : IEntryType
    {
        public EntryType Type => EntryType.TC_Flag;

        // --- Paraméterek ---
        private const double MinSlope = 0.0005;
        private const int ImpulseLookback = 5;
        private const int MIN_SCORE = 50;

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
            // 1️⃣ IMPULSE – HARD (M5)
            // =========================================================
            if (!ctx.HasImpulse_M5)
            {
                eval.Reason += "NoImpulse;";
                return eval;
            }

            if (ctx.M5.Count < ImpulseLookback + 1)
            {
                eval.Reason += "NotEnoughBars;";
                return eval;
            }

            double impulseMove =
                ctx.M5.ClosePrices.LastValue -
                ctx.M5.ClosePrices[ctx.M5.Count - 1 - ImpulseLookback];

            // Gyenge impulse kiszűrése
            if (Math.Abs(impulseMove) < ctx.AtrM5 * 0.8)
            {
                eval.Reason += "WeakImpulse;";
                return eval;
            }

            TradeDirection impulseDirection =
                impulseMove > 0 ? TradeDirection.Long : TradeDirection.Short;

            score += 25;

            // =========================================================
            // 2️⃣ FLAG STRUKTÚRA – HARD (M5)
            // =========================================================
            if (!ctx.IsValidFlagStructure_M5)
            {
                eval.Reason += "NoFlagStructure;";
                return eval;
            }

            score += 25;

            // =========================================================
            // 3️⃣ TREND + IMPULSE ALIGNMENT – HARD
            // =========================================================
            bool trendUp = ctx.Ema21Slope_M15 > MinSlope;
            bool trendDown = ctx.Ema21Slope_M15 < -MinSlope;

            if (trendUp && impulseDirection == TradeDirection.Long)
            {
                eval.Direction = TradeDirection.Long;
                score += 25;
            }
            else if (trendDown && impulseDirection == TradeDirection.Short)
            {
                eval.Direction = TradeDirection.Short;
                score += 25;
            }
            else
            {
                eval.Reason += "TrendImpulseMismatch;";
                return eval;
            }

            // =========================================================
            // 4️⃣ M1 FLAG BREAK TRIGGER – HARD
            // =========================================================
            if (!ctx.M1FlagBreakTrigger)
            {
                eval.Reason += "NoM1Break;";
                return eval;
            }

            score += 30;

            // =========================================================
            // 5️⃣ MINŐSÉGI BOOSTOK – SOFT
            // =========================================================
            if (ctx.IsAtrExpanding_M5)
                score += 5;

            if (ctx.IsVolumeIncreasing_M5)
                score += 5;

            // =========================================================
            // 6️⃣ MIN SCORE – ENTRYTYPE SZINT
            // =========================================================
            eval.Score = score;
            eval.IsValid = score >= MIN_SCORE;

            if (!eval.IsValid)
                eval.Reason += $"ScoreBelowMin({score});";

            return eval;
        }
    }
}
