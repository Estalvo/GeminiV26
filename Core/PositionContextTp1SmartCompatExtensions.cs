using System;
using System.Collections.Generic;
using System.Reflection;

namespace GeminiV26.Core
{
    /// <summary>
    /// Compatibility shim for TP1 smart-exit fields so exit managers can compile
    /// against both older and newer PositionContext shapes.
    /// </summary>
    public static class PositionContextTp1SmartCompatExtensions
    {
        private sealed class CompatState
        {
            public int Tp1HitBarIndex = -1;
            public bool Tp1SmartExitHit;
            public string Tp1SmartExitType = string.Empty;
            public string Tp1SmartExitReason = string.Empty;
            public double? Tp1SmartExitR;
            public int? Tp1SmartBarsSinceTp1;
        }

        private static readonly Dictionary<long, CompatState> _fallbackByPositionId = new Dictionary<long, CompatState>();

        private static CompatState GetFallbackState(PositionContext ctx)
        {
            if (!_fallbackByPositionId.TryGetValue(ctx.PositionId, out var state))
            {
                state = new CompatState();
                _fallbackByPositionId[ctx.PositionId] = state;
            }

            return state;
        }

        private static PropertyInfo FindProperty(string propertyName)
        {
            return typeof(PositionContext).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        }

        public static void SetTp1HitBarIndex(this PositionContext ctx, int value)
        {
            var p = FindProperty("Tp1HitBarIndex");
            if (p != null && p.CanWrite)
            {
                p.SetValue(ctx, value);
                return;
            }

            GetFallbackState(ctx).Tp1HitBarIndex = value;
        }

        public static int GetTp1HitBarIndex(this PositionContext ctx)
        {
            var p = FindProperty("Tp1HitBarIndex");
            if (p != null)
            {
                var raw = p.GetValue(ctx);
                if (raw is int v)
                    return v;
            }

            return GetFallbackState(ctx).Tp1HitBarIndex;
        }

        public static void SetTp1SmartExitHit(this PositionContext ctx, bool value)
        {
            var p = FindProperty("Tp1SmartExitHit");
            if (p != null && p.CanWrite)
            {
                p.SetValue(ctx, value);
                return;
            }

            GetFallbackState(ctx).Tp1SmartExitHit = value;
        }

        public static bool GetTp1SmartExitHit(this PositionContext ctx)
        {
            var p = FindProperty("Tp1SmartExitHit");
            if (p != null)
            {
                var raw = p.GetValue(ctx);
                if (raw is bool v)
                    return v;
            }

            return GetFallbackState(ctx).Tp1SmartExitHit;
        }

        public static void SetTp1SmartExitType(this PositionContext ctx, string value)
        {
            var p = FindProperty("Tp1SmartExitType");
            if (p != null && p.CanWrite)
            {
                p.SetValue(ctx, value ?? string.Empty);
                return;
            }

            GetFallbackState(ctx).Tp1SmartExitType = value ?? string.Empty;
        }

        public static string GetTp1SmartExitType(this PositionContext ctx)
        {
            var p = FindProperty("Tp1SmartExitType");
            if (p != null)
            {
                var raw = p.GetValue(ctx) as string;
                return raw ?? string.Empty;
            }

            return GetFallbackState(ctx).Tp1SmartExitType;
        }

        public static void SetTp1SmartExitReason(this PositionContext ctx, string value)
        {
            var p = FindProperty("Tp1SmartExitReason");
            if (p != null && p.CanWrite)
            {
                p.SetValue(ctx, value ?? string.Empty);
                return;
            }

            GetFallbackState(ctx).Tp1SmartExitReason = value ?? string.Empty;
        }

        public static string GetTp1SmartExitReason(this PositionContext ctx)
        {
            var p = FindProperty("Tp1SmartExitReason");
            if (p != null)
            {
                var raw = p.GetValue(ctx) as string;
                return raw ?? string.Empty;
            }

            return GetFallbackState(ctx).Tp1SmartExitReason;
        }

        public static void SetTp1SmartExitR(this PositionContext ctx, double? value)
        {
            var p = FindProperty("Tp1SmartExitR");
            if (p != null && p.CanWrite)
            {
                p.SetValue(ctx, value);
                return;
            }

            GetFallbackState(ctx).Tp1SmartExitR = value;
        }

        public static double? GetTp1SmartExitR(this PositionContext ctx)
        {
            var p = FindProperty("Tp1SmartExitR");
            if (p != null)
            {
                var raw = p.GetValue(ctx);
                if (raw is double v)
                    return v;
                return raw as double?;
            }

            return GetFallbackState(ctx).Tp1SmartExitR;
        }

        public static void SetTp1SmartBarsSinceTp1(this PositionContext ctx, int? value)
        {
            var p = FindProperty("Tp1SmartBarsSinceTp1");
            if (p != null && p.CanWrite)
            {
                p.SetValue(ctx, value);
                return;
            }

            GetFallbackState(ctx).Tp1SmartBarsSinceTp1 = value;
        }

        public static int? GetTp1SmartBarsSinceTp1(this PositionContext ctx)
        {
            var p = FindProperty("Tp1SmartBarsSinceTp1");
            if (p != null)
            {
                var raw = p.GetValue(ctx);
                if (raw is int v)
                    return v;
                return raw as int?;
            }

            return GetFallbackState(ctx).Tp1SmartBarsSinceTp1;
        }
    }
}
