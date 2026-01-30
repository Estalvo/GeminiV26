using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Data;
using GeminiV26.Data.Models;

namespace GeminiV26
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.FullAccess)]
    public class GeminiV26Bot : Robot
    {
        private TradeCore _core;

        // 🔹 BAR LOGGING
        private BarLogger _barLogger;

        // 🔹 EVENT LOGGING
        private EventLogger _eventLogger;

        // 🔹 TRADE LOGGING
        private TradeLogger _tradeLogger;

        // 🔹 SESSION
        public string CurrentSessionId { get; private set; }

        protected override void OnStart()
        {
            Print("======================================");
            Print("🚀 GEMINI V26 DEMO BOT STARTED");
            Print("❌ NOT legacy Gemini");
            Print($"📊 Symbol: {SymbolName}");
            Print($"⏱ Timeframe: {TimeFrame}");
            Print("======================================");

            // =========================
            // 🔹 SESSION (restart = új session)
            // =========================
            CurrentSessionId = Guid.NewGuid().ToString();

            // =========================
            // 🔹 CORE
            // =========================
            _core = new TradeCore(this);

            // =========================
            // 🔹 LOGGERS
            // =========================
            _barLogger = new BarLogger(this);
            _eventLogger = new EventLogger(SymbolName);
            _tradeLogger = new TradeLogger(SymbolName);

            // =========================
            // 🔔 BOT START EVENT
            // =========================
            _eventLogger.Log(new EventRecord
            {
                EventTimestamp = Server.Time,
                Symbol = SymbolName,
                EventType = "BotStart",
                Reason = $"Started on {TimeFrame}"
            });

            // =========================
            // 🔹 REHYDRATE (XAU)
            // =========================
            if (SymbolName.Contains("XAU"))
            {
                _core.XauExitManager.RehydrateFromLivePositions(this);
            }
        }

        protected override void OnBar()
        {
            // Trade logika (entry, bar-alapú exit)
            _core.OnBar();
        }

        protected override void OnTick()
        {
            // M1 + M5 BAR LOGGING (TF-független)
            _barLogger.OnTick();

            // ExitManager tick-alapú kezelése (TP1, trailing)
            _core.OnTick();
        }

        protected override void OnStop()
        {
            // =========================
            // 🔔 BOT STOP EVENT
            // =========================
            _eventLogger.Log(new EventRecord
            {
                EventTimestamp = Server.Time,
                Symbol = SymbolName,
                EventType = "BotStop"
            });

            _core.OnStop();
            Print("GeminiV26Bot STOP");
        }
    }
}
