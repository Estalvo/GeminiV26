using System;
using System.Collections.Generic;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Exit;

namespace GeminiV26.Instruments.Common
{
    /// <summary>
    /// Phase 3.7+
    /// Globális Exit Manager alap.
    /// - Context lifecycle kezelése
    /// - Rehydrate logika
    /// - State guardok
    /// Instrument-specifikus exit NEM itt van.
    /// </summary>
    public abstract class BaseExitManager
    {
        protected readonly Robot Bot;

        // PositionId -> Context
        protected readonly Dictionary<long, PositionContext> Contexts = new();

        protected BaseExitManager(Robot bot)
        {
            Bot = bot;
        }

        // =====================================================
        // REGISTRATION
        // =====================================================
        public virtual void RegisterContext(PositionContext ctx)
        {
            if (ctx == null)
                return;

            Contexts[ctx.PositionId] = ctx;
        }

        // =====================================================
        // REHYDRATE ENTRY POINT
        // =====================================================
        public virtual void RehydrateFromLivePositions(Robot bot)
        {
            foreach (var pos in bot.Positions)
            {
                if (!Accepts(pos))
                    continue;

                // már van context (pl. hot reload)
                if (Contexts.ContainsKey(pos.Id))
                    continue;

                var ctx = RehydrateContext(pos);
                if (ctx == null)
                    continue;

                ctx.IsRehydrated = true;
                Contexts[pos.Id] = ctx;

                Bot.Print($"[{pos.SymbolName} REHYDRATE] pos={pos.Id}");
            }
        }

        // =====================================================
        // COMMON HELPERS
        // =====================================================
        protected bool TryGetContext(Position pos, out PositionContext ctx)
            => Contexts.TryGetValue(pos.Id, out ctx);

        protected void RemoveContext(long positionId)
        {
            if (Contexts.ContainsKey(positionId))
                Contexts.Remove(positionId);
        }

        // =====================================================
        // STATE GUARDS (KRITIKUS)
        // =====================================================

        /// <summary>
        /// TP1 már lefutott? Akkor újra nem futhat.
        /// </summary>
        protected bool GuardTp1AlreadyHit(PositionContext ctx)
            => ctx.Tp1Hit;

        /// <summary>
        /// Trailing csak TP1 után és aktív módban.
        /// </summary>
        protected bool GuardTrailingAllowed(PositionContext ctx)
            => ctx.Tp1Hit && ctx.TrailingMode != TrailingMode.None;

        /// <summary>
        /// BE csak TP1 után.
        /// </summary>
        protected bool GuardBeAllowed(PositionContext ctx)
            => ctx.Tp1Hit && ctx.BeMode != BeMode.None;

        // =====================================================
        // POSITION SYNC
        // =====================================================
        protected void SyncFromPosition(Position pos, PositionContext ctx)
        {
            ctx.RemainingVolumeInUnits = pos.VolumeInUnits;

            if (pos.StopLoss.HasValue)
                ctx.LastStopLossPrice = pos.StopLoss.Value;
        }

        // =====================================================
        // EXIT DISPATCH (ENTRY POINTS)
        // =====================================================
        public abstract void OnTick();

        public abstract void OnBar(Position pos);

        // =====================================================
        // ABSTRACT CONTRACTS
        // =====================================================

        /// <summary>
        /// Ez az ExitManager kezeli-e ezt a pozíciót?
        /// </summary>
        protected abstract bool Accepts(Position pos);

        /// <summary>
        /// Rehydrate során Position -> Context visszaépítése.
        /// Instrument-specifikus.
        /// </summary>
        protected abstract PositionContext RehydrateContext(Position pos);
    }
}
