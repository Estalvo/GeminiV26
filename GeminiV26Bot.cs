using System;
using cAlgo.API;
using GeminiV26.Core;
using GeminiV26.Core.Runtime;
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

        //private TelemetryRecorder _telemetry;

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
            BotRestartState.Initialize(Server.Time, Bars != null ? Bars.Count : 0);

            // =========================
            // 🔹 CORE
            // =========================
            _core = new TradeCore(this);
            _core.Init();

            // =========================
            // 🔹 LOGGERS
            // =========================
            _barLogger = new BarLogger(this);
            _eventLogger = new EventLogger(SymbolName);
            _tradeLogger = new TradeLogger(SymbolName);
            //_telemetry = new TelemetryRecorder(SymbolName);
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
            // 🔹 STARTUP REHYDRATE
            // =========================
            _core.RehydrateOpenPositions();
        }

        protected override void OnBar()
        {
            BotRestartState.Update(Server.Time, Bars != null ? Bars.Count : 0);
            // Trade logika (entry, bar-alapú exit)
            _core.OnBar();
        }

        protected override void OnTick()
        {
            BotRestartState.Update(Server.Time, Bars != null ? Bars.Count : 0);
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
