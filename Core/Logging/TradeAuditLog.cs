using System;
using System.Globalization;
using cAlgo.API;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.Logging
{
    internal static class TradeAuditLog
    {
        public static void EnsureAttemptId(EntryContext ctx, DateTime serverTime)
        {
            if (ctx == null || !string.IsNullOrWhiteSpace(ctx.EntryAttemptId))
                return;

            string symbol = string.IsNullOrWhiteSpace(ctx.Symbol) ? "SYM" : ctx.Symbol.Trim().ToUpperInvariant();
            string head = symbol.Length <= 3 ? symbol : symbol.Substring(0, 3);
            long millis = serverTime.Ticks / TimeSpan.TicksPerMillisecond;
            ctx.EntryAttemptId = $"{head}{ToBase36(Math.Abs(millis % 2176782336L)).PadLeft(6, '0')}";
        }

        public static string BuildDirectionSnapshot(EntryContext ctx, EntryEvaluation entry)
        {
            TradeDirection htfDirection = ResolveHtfDirection(ctx);
            int htfConfidence = ResolveHtfConfidence(ctx);

            return "[DIR]\n" +
                   $"logicBias={ctx?.LogicBiasDirection ?? TradeDirection.None}\n" +
                   $"routedDirection={entry?.Direction ?? TradeDirection.None}\n" +
                   $"finalDirection={ctx?.FinalDirection ?? TradeDirection.None}\n" +
                   $"htfDirection={htfDirection}\n" +
                   $"htfConfidence={htfConfidence}";
        }

        public static string BuildDirectionSnapshot(PositionContext ctx)
        {
            return "[DIR]\n" +
                   $"logicBias={ctx?.FinalDirection ?? TradeDirection.None}\n" +
                   $"routedDirection={ctx?.FinalDirection ?? TradeDirection.None}\n" +
                   $"finalDirection={ctx?.FinalDirection ?? TradeDirection.None}\n" +
                   "htfDirection=None\n" +
                   "htfConfidence=0";
        }

        public static string BuildEntrySnapshot(
            Robot bot,
            EntryContext ctx,
            EntryEvaluation entry,
            int logicConfidence,
            int finalConfidence,
            int statePenalty,
            int riskFinal)
        {
            string symbol = entry?.Symbol ?? ctx?.Symbol ?? bot?.SymbolName ?? "UNKNOWN";
            string attemptId = ctx?.EntryAttemptId ?? string.Empty;
            TradeDirection htfDirection = ResolveHtfDirection(ctx);
            int htfConfidence = ResolveHtfConfidence(ctx);

            return "[ENTRY SNAPSHOT]\n" +
                   $"symbol={symbol}\n" +
                   $"attemptId={attemptId}\n" +
                   $"serverTime={bot?.Server.Time.ToString("O", CultureInfo.InvariantCulture)}\n" +
                   $"barsSinceStart={ctx?.BarsSinceStart ?? 0}\n" +
                   $"regime={ResolveRegime(ctx)}\n" +
                   $"setupType={entry?.Type}\n" +
                   $"entryType={entry?.Type}\n" +
                   $"direction={ctx?.FinalDirection ?? entry?.Direction ?? TradeDirection.None}\n" +
                   $"entryScore={entry?.Score ?? 0}\n" +
                   $"logicConfidence={logicConfidence}\n" +
                   $"finalConfidence={finalConfidence}\n" +
                   $"statePenalty={statePenalty}\n" +
                   $"riskFinal={riskFinal}\n" +
                   $"atr={ctx?.AtrM5 ?? 0:0.#####}\n" +
                   $"adx={ctx?.Adx_M5 ?? 0:0.##}\n" +
                   $"htfDirection={htfDirection}\n" +
                   $"htfConfidence={htfConfidence}\n" +
                   $"restartPhase={ResolveRestartPhase(ctx)}";
        }

        public static string BuildContextCreate(PositionContext ctx)
        {
            return "[CTX][CREATE]\n" +
                   $"positionId={ctx?.PositionId ?? 0}\n" +
                   $"finalDirection={ctx?.FinalDirection ?? TradeDirection.None}\n" +
                   $"tp1Hit={ToLower(ctx?.Tp1Hit ?? false)}\n" +
                   $"beMoved={ToLower(ctx?.BeActivated ?? false)}\n" +
                   $"trailActive={ToLower(ctx?.TrailingActivated ?? false)}";
        }

        public static string BuildOpenSnapshot(PositionContext ctx, double? sl, double? tp, double volumeUnits)
        {
            return "[OPEN]\n" +
                   $"entryPrice={FormatNumber(ctx?.EntryPrice)}\n" +
                   $"sl={FormatNumber(sl)}\n" +
                   $"tp={FormatNumber(tp)}\n" +
                   $"volumeUnits={volumeUnits:0.##}";
        }

        public static string BuildStateSnapshot(PositionContext ctx, Position pos, Symbol symbol)
        {
            if (ctx == null || pos == null || symbol == null)
                return string.Empty;

            double pnlPips = 0;
            if (ctx.FinalDirection == TradeDirection.Long)
                pnlPips = (symbol.Bid - pos.EntryPrice) / symbol.PipSize;
            else if (ctx.FinalDirection == TradeDirection.Short)
                pnlPips = (pos.EntryPrice - symbol.Ask) / symbol.PipSize;

            return "[STATE]\n" +
                   $"barsOpen={ctx.BarsSinceEntryM5}\n" +
                   $"pnlPips={pnlPips:0.##}\n" +
                   $"mfeR={ctx.MfeR:0.##}\n" +
                   $"maeR={ctx.MaeR:0.##}\n" +
                   $"tp1Hit={ToLower(ctx.Tp1Hit)}\n" +
                   $"beMoved={ToLower(ctx.BeActivated)}\n" +
                   $"trailActive={ToLower(ctx.TrailingActivated)}\n" +
                   $"trailSteps={ctx.TrailSteps}\n" +
                   $"regime={ResolveStateRegime(ctx)}";
        }

        public static string BuildExitSnapshot(PositionContext ctx, Position pos, string reason, DateTime exitTime, double exitPrice)
        {
            return "[EXIT SNAPSHOT]\n" +
                   $"symbol={pos?.SymbolName ?? ctx?.Symbol ?? "UNKNOWN"}\n" +
                   $"positionId={pos?.Id ?? ctx?.PositionId ?? 0}\n" +
                   $"entryTime={ctx?.EntryTime.ToString("O", CultureInfo.InvariantCulture)}\n" +
                   $"exitTime={exitTime.ToString("O", CultureInfo.InvariantCulture)}\n" +
                   $"barsOpen={ctx?.BarsSinceEntryM5 ?? 0}\n" +
                   $"entryPrice={FormatNumber(ctx?.EntryPrice ?? pos?.EntryPrice)}\n" +
                   $"exitPrice={FormatNumber(exitPrice)}\n" +
                   $"mfeR={(ctx?.MfeR ?? 0):0.##}\n" +
                   $"maeR={(ctx?.MaeR ?? 0):0.##}\n" +
                   $"tp1Hit={ToLower(ctx?.Tp1Hit ?? false)}\n" +
                   $"beMoved={ToLower(ctx?.BeActivated ?? false)}\n" +
                   $"trailActive={ToLower(ctx?.TrailingActivated ?? false)}\n" +
                   $"trailSteps={ctx?.TrailSteps ?? 0}\n" +
                   $"reason={reason}";
        }

        public static string BuildCleanup(long positionId, string reason)
        {
            return "[CLEANUP]\n" +
                   $"reason={reason}\n" +
                   $"positionId={positionId}";
        }

        private static string ResolveRegime(EntryContext ctx)
        {
            if (ctx?.MarketState != null)
            {
                if (ctx.MarketState.IsRange)
                    return "Range";
                if (ctx.MarketState.IsTrend)
                    return "Trend";
            }

            if (ctx?.TransitionValid == true)
                return "Transition";

            return "Unknown";
        }

        private static string ResolveStateRegime(PositionContext ctx)
        {
            if (ctx == null)
                return "Unknown";

            if (ctx.TrailingActivated)
                return "Trailing";
            if (ctx.Tp1Hit)
                return "PostTp1";
            return "Open";
        }

        private static string ResolveRestartPhase(EntryContext ctx)
        {
            int barsSinceStart = ctx?.BarsSinceStart ?? 0;
            if (barsSinceStart <= 2)
                return "HARD";
            if (barsSinceStart <= 6)
                return "SOFT";
            return "NONE";
        }

        private static TradeDirection ResolveHtfDirection(EntryContext ctx)
        {
            if (ctx == null)
                return TradeDirection.None;
            if (ctx.FxHtfAllowedDirection != TradeDirection.None)
                return ctx.FxHtfAllowedDirection;
            if (ctx.CryptoHtfAllowedDirection != TradeDirection.None)
                return ctx.CryptoHtfAllowedDirection;
            if (ctx.IndexHtfAllowedDirection != TradeDirection.None)
                return ctx.IndexHtfAllowedDirection;
            if (ctx.MetalHtfAllowedDirection != TradeDirection.None)
                return ctx.MetalHtfAllowedDirection;
            return TradeDirection.None;
        }

        private static int ResolveHtfConfidence(EntryContext ctx)
        {
            if (ctx == null)
                return 0;

            double confidence01 = Math.Max(
                Math.Max(ctx.FxHtfConfidence01, ctx.CryptoHtfConfidence01),
                Math.Max(ctx.IndexHtfConfidence01, ctx.MetalHtfConfidence01));

            return (int)Math.Round(confidence01 * 100.0, MidpointRounding.AwayFromZero);
        }

        private static string ToLower(bool value) => value ? "true" : "false";

        private static string FormatNumber(double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
                return "NA";

            return value.Value.ToString("0.#####", CultureInfo.InvariantCulture);
        }

        private static string ToBase36(long value)
        {
            const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            if (value <= 0)
                return "0";

            char[] buffer = new char[16];
            int index = buffer.Length;

            while (value > 0)
            {
                buffer[--index] = alphabet[(int)(value % 36)];
                value /= 36;
            }

            return new string(buffer, index, buffer.Length - index);
        }
    }
}
