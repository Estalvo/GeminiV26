// LEGACY - not used since Phase 3.8

// =========================================================
// GEMINI V26 – RiskSizerFactory
// Rulebook 1.0 COMPLIANT
//
// SZEREP:
// - Instrument → IInstrumentRiskSizer leképezés
// - CSAK wiring / factory logika
//
// FONTOS:
// - NEM számol risket
// - NEM használ score-t
// - NEM használ FinalConfidence-et
// - NEM gate-el belépést
//
// A RiskSizerFactory kizárólag:
// - kiválasztja a megfelelő instrument-specifikus RiskSizer-t
// - biztosítja, hogy MINDEN instrument explicit sizerrel rendelkezzen
//
// Risk döntések HELYE:
// - PositionContext (FinalConfidence)
// - InstrumentRiskSizer implementációk
//
// Ez a fájl szándékosan buta.
// Ha itt logika jelenik meg, az ARCHITEKTURÁLIS HIBA.
// =========================================================

using System;
using System.Diagnostics;
using GeminiV26.Instruments.XAUUSD;
using GeminiV26.Instruments.NAS100;
using GeminiV26.Instruments.US30;
using GeminiV26.Instruments.EURUSD;
using GeminiV26.Instruments.USDJPY;
using GeminiV26.Instruments.GBPUSD;
using GeminiV26.Instruments.BTCUSD;

namespace GeminiV26.Risk
{
    public static class RiskSizerFactory
    {
        public static IInstrumentRiskSizer Create(string symbol)
        {
            if (symbol.Contains("XAU"))
            {
                var s = new XauInstrumentRiskSizer();
                Debug.Assert(s is XauInstrumentRiskSizer, "XAU MUST use XauInstrumentRiskSizer");
                return s;
            }

            if (symbol.Contains("NAS"))
            {
                var s = new NasInstrumentRiskSizer();
                Debug.Assert(s is NasInstrumentRiskSizer, "NAS MUST use NasInstrumentRiskSizer");
                return s;
            }

            if (symbol.Contains("US30"))
            {
                var s = new Us30InstrumentRiskSizer();
                Debug.Assert(s is Us30InstrumentRiskSizer, "US30 MUST use Us30InstrumentRiskSizer");
                return s;
            }

            if (symbol.Contains("EURUSD"))
            {
                var s = new EurUsdInstrumentRiskSizer();
                Debug.Assert(s is EurUsdInstrumentRiskSizer, "EURUSD MUST use EurUsdInstrumentRiskSizer");
                return s;
            }

            if (symbol.Contains("USDJPY"))
            {
                var s = new UsdJpyInstrumentRiskSizer();
                Debug.Assert(s is UsdJpyInstrumentRiskSizer, "USDJPY MUST use UsdJpyInstrumentRiskSizer");
                return s;
            }

            if (symbol.Contains("GBPUSD"))
            {
                var s = new GbpUsdInstrumentRiskSizer();
                Debug.Assert(s is GbpUsdInstrumentRiskSizer, "GBPUSD MUST use GbpUsdInstrumentRiskSizer");
                return s;
            }

            if (symbol.Contains("BTC"))
            {
                var s = new BtcUsdInstrumentRiskSizer();
                Debug.Assert(s is BtcUsdInstrumentRiskSizer, "BTCUSD MUST use BtcUsdInstrumentRiskSizer");
                return s;
            }

            Debug.Assert(false, $"UNSUPPORTED SYMBOL IN RiskSizerFactory: {symbol}");
            throw new InvalidOperationException($"Unsupported symbol: {symbol}");
        }
    }
}
