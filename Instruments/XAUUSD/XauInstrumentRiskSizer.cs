// =========================================================
// GEMINI V26 – XAUUSD Instrument RiskSizer
// Phase 3.7.3 – RULEBOOK 1.0 COMPLIANT
//
// SZEREP:
// - XAUUSD instrument-specifikus risk adapter
// - FinalConfidence (0–100) → risk policy (risk%, SL ATR mult, TP struktúra, lot cap)
//
// RULEBOOK 1.0:
// - NEM belépési gate
// - NEM score-alapú
// - NEM tilt trade-et (nincs threshold, nincs return 0 mint "stop")
// - Alacsony confidence → kisebb risk, de nem nulla
//
// MEGJEGYZÉS AZ "EXTRA" METÓDUSOKRÓL:
// - Ezek nem részei az IInstrumentRiskSizer interface-nek,
//   de a XAU executor számára kényelmi számításokat adnak:
//   SL price distance, TP2 price, volume in units.
// - Ezek is FinalConfidence-alapúak, és defenzíven számolnak.
// - Ha nem számolható értelmesen, 0-t adhatnak vissza,
//   de ez NEM "gate" filozófia, hanem számítási fail-safe.
//   A trade indításának gate-jei kizárólag TradeCore-ban vannak.
// =========================================================

using cAlgo.API;
using GeminiV26.Core.Entry;
using GeminiV26.Risk;
using System;

namespace GeminiV26.Instruments.XAUUSD
{
    public class XauInstrumentRiskSizer : IInstrumentRiskSizer
    {
        // =========================================================
        // RISK %
        // =========================================================
        public double GetRiskPercent(int finalConfidence)
        {
            // Bucketek – NEM gate
            if (finalConfidence >= 85) return 0.22;
            if (finalConfidence >= 75) return 0.16;
            return 0.10;
        }

        // =========================================================
        // SL ATR MULT
        // =========================================================
        public double GetStopLossAtrMultiplier(int finalConfidence, EntryType entryType)
        {
            // XAU M5: feszesebb magas conf-nál, tágabb alacsonynál
            // (EntryType finomhangolás később, most marad egyszerű és stabil)
            if (finalConfidence >= 85) return 2.2;
            if (finalConfidence >= 75) return 2.6;
            return 3.0;
        }

        // =========================================================
        // TP MATRIX (R-alapú)
        // =========================================================
        public void GetTakeProfit(
            int finalConfidence,
            out double tp1R,
            out double tp1Ratio,
            out double tp2R,
            out double tp2Ratio
        )
        {
            // XAU: impulse-first, momentum sensitive
            tp1R = 0.30;

            // =========================
            // TP1 RATIO – DINAMIKUS
            // =========================
            if (finalConfidence >= 85)
                tp1Ratio = 0.45;   // 55% fut
            else if (finalConfidence >= 75)
                tp1Ratio = 0.55;
            else
                tp1Ratio = 0.70;   // védekező

            // =========================
            // TP2 R – HELYES IRÁNY
            // =========================
            if (finalConfidence >= 85)
                tp2R = 1.6;
            else if (finalConfidence >= 75)
                tp2R = 1.2;
            else
                tp2R = 0.9;

            tp2Ratio = 1.0 - tp1Ratio;
        }

        // =========================================================
        // LOT CAP (XAU specifikus)
        // =========================================================
        public double GetLotCap(int finalConfidence)
        {
            // Safety hard cap (nem gate)
            // A lot cap lehet confidence-függő, de éles indulásnál stabil cap jobb.
            return 0.10;
        }

        // =========================================================
        // EXTRA: SL PRICE DISTANCE (ATR alapú) – executor helper
        // =========================================================
        public double CalculateStopLossPriceDistance(
            Robot bot,
            int finalConfidence,
            EntryType entryType
        )
        {
            var m5 = bot.MarketData.GetBars(TimeFrame.Minute5);
            if (m5 == null || m5.Count < 30)
                return 0;

            var atr = bot.Indicators.AverageTrueRange(14, MovingAverageType.Exponential).Result.LastValue;
            if (atr <= 0 || double.IsNaN(atr) || double.IsInfinity(atr))
                return 0;

            double atrMult = GetStopLossAtrMultiplier(finalConfidence, entryType);

            // XAU-n adunk egy kis fix buffert, hogy ne legyen túl szoros a spread/noise miatt
            double buffer = 0.25; // USD

            double slDistance = (atr * atrMult) + buffer;
            return slDistance > 0 ? slDistance : 0;
        }

        // =========================================================
        // EXTRA: TP2 PRICE (TP matrix + provided SL distance)
        // =========================================================
        public double CalculateTp2PriceFromSlDistance(
            Robot bot,
            TradeType tradeType,
            int finalConfidence,
            double slPriceDistance
        )
        {
            if (slPriceDistance <= 0)
                return 0;

            GetTakeProfit(finalConfidence, out _, out _, out double tp2R, out _);

            // EntryPrice-t az executor úgyis ismeri, itt egyszerűen a pillanatnyi árat használjuk.
            // (Ha executor átadja az entryPrice-t, még tisztább, de most minimál refaktor.)
            double entryPrice = tradeType == TradeType.Buy ? bot.Symbol.Ask : bot.Symbol.Bid;

            double price = tradeType == TradeType.Buy
                ? entryPrice + slPriceDistance * tp2R
                : entryPrice - slPriceDistance * tp2R;

            return price;
        }

        // =========================================================
        // EXTRA: VOLUME IN UNITS (XAU helyesen) – executor helper
        // =========================================================
        public long CalculateVolumeInUnits(
            Robot bot,
            int finalConfidence,
            double slPriceDistance
        )
        {
            if (slPriceDistance <= 0)
                return 0;

            double riskPercent = GetRiskPercent(finalConfidence);
            var symbol = bot.Symbol;

            double riskMoney = bot.Account.Balance * (riskPercent / 100.0);
            if (riskMoney <= 0)
                return 0;

            // XAU: pénz / (ármozgás * (tickValue/tickSize) * units)
            double valuePerPricePerUnit = symbol.TickValue / symbol.TickSize;
            if (valuePerPricePerUnit <= 0)
                return 0;

            double rawUnits = riskMoney / (slPriceDistance * valuePerPricePerUnit);
            if (rawUnits <= 0)
                return 0;

            double units = symbol.NormalizeVolumeInUnits(rawUnits, RoundingMode.Down);
            if (units <= 0)
                return 0;

            // Cap (lot → units)
            double maxLot = GetLotCap(finalConfidence);
            double maxUnits = symbol.NormalizeVolumeInUnits(maxLot * symbol.LotSize, RoundingMode.Down);

            if (maxUnits > 0 && units > maxUnits)
                units = maxUnits;

            return units > 0 ? (long)units : 0;
        }
    }
}
