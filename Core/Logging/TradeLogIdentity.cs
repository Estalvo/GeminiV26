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

            return WithTempId(message, ctx.TempId);
        }

        public static string WithTempId(string message, string tempId)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(tempId) || message.Contains("tempId=", StringComparison.Ordinal))
                return message;

            return InsertFields(message, $"tempId={tempId}");
        }

        public static string WithPositionIds(string message, PositionContext ctx, Position pos = null)
        {
            long posId = 0;
            if (pos != null && pos.Id > 0)
                posId = pos.Id;
            else if (ctx != null && ctx.PositionId > 0)
                posId = ctx.PositionId;

            return WithPositionIds(message, posId > 0 ? posId : (long?)null, ctx?.TempId);
        }

        public static string WithPositionIds(string message, long? posId, string tempId = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return message;

            message = EnsurePositionPrefix(message, posId);

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

        private static string EnsurePositionPrefix(string message, long? posId)
        {
            string prefix = posId.HasValue && posId.Value > 0
                ? $"[POS {posId.Value}]"
                : "[POS ?]";

            if (message.StartsWith(prefix, StringComparison.Ordinal))
                return message;

            return $"{prefix} {message}";
        }
    }
}
