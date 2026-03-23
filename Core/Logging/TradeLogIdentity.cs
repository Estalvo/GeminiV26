using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core.Entry;

namespace GeminiV26.Core.Logging
{
    internal static class TradeLogIdentity
    {
        public static string WithTempId(string message, EntryContext ctx)
        {
            if (ctx == null)
                return message;

            return WithAttemptPrefix(message, ctx.Symbol, ctx.EntryAttemptId);
        }

        public static string WithTempId(string message, string tempId)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            if (string.IsNullOrWhiteSpace(tempId))
                return message;

            string prefixed = EnsureAttemptPrefix(message, null, tempId);
            if (prefixed.Contains("tempId=", StringComparison.Ordinal))
                return prefixed;

            return InsertFields(prefixed, $"tempId={tempId}");
        }

        public static string WithAttemptPrefix(string message, string symbol, string attemptId)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            message = EnsureAttemptPrefix(message, symbol, attemptId);

            if (string.IsNullOrWhiteSpace(attemptId) || message.Contains("attemptId=", StringComparison.Ordinal))
                return message;

            return InsertFields(message, $"attemptId={attemptId}");
        }

        public static string WithPositionIds(string message, PositionContext ctx, Position pos = null)
        {
            long posId = 0;
            string symbol = null;
            if (pos != null && pos.Id > 0)
            {
                posId = pos.Id;
                symbol = pos.SymbolName;
            }
            else if (ctx != null && ctx.PositionId > 0)
            {
                posId = ctx.PositionId;
                symbol = ctx.Symbol;
            }

            return WithPositionIds(message, posId > 0 ? posId : (long?)null, ctx?.TempId, symbol);
        }

        public static string WithPositionIds(string message, long? posId, string tempId = null, string symbol = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            message = EnsurePositionPrefix(message, symbol, posId);

            var fields = new List<string>();
            if (!string.IsNullOrWhiteSpace(tempId) && !message.Contains("tempId=", StringComparison.Ordinal))
                fields.Add($"tempId={tempId}");

            if (posId.HasValue && posId.Value > 0 && !message.Contains("posId=", StringComparison.Ordinal))
                fields.Add($"posId={posId.Value}");

            if (fields.Count == 0)
                return message;

            return InsertFields(message, string.Join(" ", fields));
        }

        private static string InsertFields(string message, string fields)
        {
            if (string.IsNullOrWhiteSpace(fields))
                return message;

            if (message.StartsWith("[", StringComparison.Ordinal))
            {
                int closing = message.IndexOf(']');
                if (closing >= 0)
                    return message.Insert(closing + 1, $" {fields}");
            }

            return $"{fields} {message}";
        }

        private static string EnsureAttemptPrefix(string message, string symbol, string attemptId)
        {
            string prefix = $"{EnsureSymbolPrefix(symbol)} {EnsureAttemptToken(attemptId)}";

            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return message;

            return $"{prefix} {message}";
        }

        private static string EnsurePositionPrefix(string message, string symbol, long? posId)
        {
            string prefix =
                $"{EnsureSymbolPrefix(symbol)} " +
                (posId.HasValue && posId.Value > 0
                    ? $"[POS {posId.Value}]"
                    : "[POS ?]");

            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return message;

            return $"{prefix} {message}";
        }

        private static string EnsureSymbolPrefix(string symbol)
        {
            return string.IsNullOrWhiteSpace(symbol)
                ? "[UNKNOWN]"
                : $"[{symbol.Trim().ToUpperInvariant()}]";
        }

        private static string EnsureAttemptToken(string attemptId)
        {
            return string.IsNullOrWhiteSpace(attemptId)
                ? "[ATTEMPT ?]"
                : $"[ATTEMPT {attemptId}]";
        }
    }
}
