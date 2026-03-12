using System;

namespace GeminiV26.Core
{
    public enum InstrumentClass
    {
        FX,
        INDEX,
        METAL,
        CRYPTO
    }

    public static class SymbolRouting
    {
        public static string NormalizeSymbol(string symbol)
        {
            var s = (symbol ?? string.Empty)
                .ToUpperInvariant()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty);

            return s switch
            {
                "US100" => "NAS100",
                "USTECH100" => "NAS100",
                "USTECH" => "NAS100",
                "GER" => "GER40",
                "DE40" => "GER40",
                "DAX" => "GER40",
                "DAX40" => "GER40",
                "GERMANY40" => "GER40",
                "DJ30" => "US30",
                "DOW" => "US30",
                "GOLD" => "XAUUSD",
                "XAU" => "XAUUSD",
                "XBTUSD" => "BTCUSD",
                _ => s
            };
        }

        public static InstrumentClass ResolveInstrumentClass(string symbol)
        {
            var s = NormalizeSymbol(symbol);

            if (s == "XAUUSD" || s == "XAGUSD")
                return InstrumentClass.METAL;

            if (s == "BTCUSD" || s == "ETHUSD")
                return InstrumentClass.CRYPTO;

            if (s == "NAS100" || s == "US30" || s == "GER40")
                return InstrumentClass.INDEX;

            return InstrumentClass.FX;
        }
    }
}
