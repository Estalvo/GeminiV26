// =========================================================
// =========================================================
// GEMINI V26 – TradeCore
// Rulebook 1.0 – Orchestrator Layer
//
// TradeCore FELELŐSSÉGE:
// - pipeline vezérlés (build → evaluate → route → gate → execute)
// - egyetlen nyitott pozíció enforce
// - instrument routing
//
// TradeCore NEM:
// - score-ol
// - confidence-et számol
// - stratégia között dönt
// - EntryLogic-ra hallgat veto-ként
//
// SCORE / CONFIDENCE SZABÁLY:
// - EntryType → EntryScore
// - EntryLogic → LogicConfidence (csak info)
// - PositionContext → FinalConfidence (single source of truth)
//
// GATE SZABÁLY:
// - Session / Impulse gate az egyetlen HARD STOP
// - BTC/ETH esetén az ImpulseGate kötelező
//
// Ez a fájl NORMATÍV.
// Ha itt score vagy confidence gate jelenik meg, az BUG.
// =========================================================

using cAlgo.API;
using cAlgo.API.Internals;
using Gemini.Memory;
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.EntryTypes.FX;
using GeminiV26.EntryTypes.INDEX;
using GeminiV26.EntryTypes.METAL;
using GeminiV26.EntryTypes.Crypto;
using GeminiV26.Interfaces;
using GeminiV26.Core.Logging;
using GeminiV26.Instruments.XAUUSD;
using GeminiV26.Instruments.NAS100;
using GeminiV26.Instruments.US30;
using GeminiV26.Instruments.GER40;
using GeminiV26.Instruments.EURUSD;
using GeminiV26.Instruments.USDJPY;
using GeminiV26.Instruments.GBPJPY;
using GeminiV26.Instruments.NZDUSD;
using GeminiV26.Instruments.USDCAD;
using GeminiV26.Instruments.USDCHF;
using GeminiV26.Instruments.GBPUSD;
using GeminiV26.Instruments.AUDUSD;
using GeminiV26.Instruments.AUDNZD;
using GeminiV26.Instruments.EURJPY;
using GeminiV26.Instruments.BTCUSD;
using GeminiV26.Instruments.ETHUSD;
using GeminiV26.Instruments.FX;
using GeminiV26.Instruments.INDEX;
using GeminiV26.Instruments.CRYPTO;
using GeminiV26.Instruments.METAL;
using System;
using System.Collections.Generic;
using GeminiV26.Core.HtfBias;
using GeminiV26.Core.Matrix;
using GeminiV26.Core.Context;
using GeminiV26.Core.Analytics;
using GeminiV26.Core.Memory;
using GeminiV26.Core.Runtime;
using GeminiV26.Core.Risk;
using System.Linq;

namespace GeminiV26.Core
{
    public class TradeCore
    {
        private readonly Robot _bot;
        private RuntimeSymbolResolver _runtimeSymbols;
        private readonly TradeRouter _router;

        private readonly EntryRouter _entryRouter;
        private readonly EntryContextBuilder _contextBuilder;
        private readonly TransitionDetector _transitionDetector;
        private readonly FlagBreakoutDetector _flagBreakoutDetector;
        private readonly List<IEntryType> _entryTypes;

        private readonly LogWriter _logWriter;
        private readonly ITradeLogger _logger;
        private readonly Dictionary<long, PositionContext> _positionContexts = new();
        private readonly Dictionary<string, ArmedSetup> _armedSetups = new();
        private readonly TradeMetaStore _tradeMetaStore = new();
        private readonly TradeStatsTracker _statsTracker;
        private readonly TradeMemoryStore _tradeMemoryStore;
        private readonly MemoryLogger _memoryLogger;
        private readonly MarketMemoryEngine _memoryEngine;
        private readonly Dictionary<string, IExitManager> _exitManagersByCanonical = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _symbolCanonical;
        private readonly InstrumentClass _instrumentClass;
        private readonly Dictionary<string, EntryTraceSummary> _entryTraceSummaries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, double> _entryBalanceByPositionId = new();
       
        private const string BotLabel = "GeminiV26";
                
        // =========================
        // Instrument components
        // NOTE: nem readonly, mert csak az adott chart instrumentumát initeljük
        // =========================

        // XAU
        private XauEntryLogic _xauEntryLogic;
        private XauInstrumentExecutor _xauExecutor;
        private XauExitManager _xauExitManager;
        public XauExitManager XauExitManager => _xauExitManager;
        private IGate _xauSessionGate;
        private IGate _xauImpulseGate;

        // NAS
        private NasEntryLogic _nasEntryLogic;
        private NasInstrumentRiskSizer _nasRiskSizer;
        private NasInstrumentExecutor _nasExecutor;
        private NasExitManager _nasExitManager;
        private IGate _nasSessionGate;
        private IGate _nasImpulseGate;

        // US30
        private Us30EntryLogic _us30EntryLogic;
        private Us30InstrumentRiskSizer _us30RiskSizer;
        private Us30InstrumentExecutor _us30Executor;
        private Us30ExitManager _us30ExitManager;
        private IGate _us30SessionGate;
        private IGate _us30ImpulseGate;

        // GER40
        private Ger40EntryLogic _ger40EntryLogic;
        private Ger40InstrumentRiskSizer _ger40RiskSizer;
        private Ger40InstrumentExecutor _ger40Executor;
        private Ger40ExitManager _ger40ExitManager;
        private IGate _ger40SessionGate;
        private IGate _ger40ImpulseGate;

        // EURUSD
        private EurUsdEntryLogic _eurUsdEntryLogic;
        private EurUsdInstrumentRiskSizer _eurUsdRiskSizer;
        private EurUsdInstrumentExecutor _eurUsdExecutor;
        private EurUsdExitManager _eurUsdExitManager;
        private IGate _eurUsdSessionGate;
        private IGate _eurUsdImpulseGate;
                
        // USDJPY
        private UsdJpyEntryLogic _usdJpyEntryLogic;
        private UsdJpyInstrumentRiskSizer _usdJpyRiskSizer;
        private UsdJpyInstrumentExecutor _usdJpyExecutor;
        private UsdJpyExitManager _usdJpyExitManager;
        private IGate _usdJpySessionGate;
        private IGate _usdJpyImpulseGate;

        // GBPUSD
        private GbpUsdEntryLogic _gbpUsdEntryLogic;
        private GbpUsdInstrumentRiskSizer _gbpUsdRiskSizer;
        private GbpUsdInstrumentExecutor _gbpUsdExecutor;
        private GbpUsdExitManager _gbpUsdExitManager;
        private IGate _gbpUsdSessionGate;
        private IGate _gbpUsdImpulseGate;

        // AUDUSD
        private AudUsdEntryLogic _audUsdEntryLogic;
        private AudUsdInstrumentRiskSizer _audUsdRiskSizer;
        private AudUsdInstrumentExecutor _audUsdExecutor;
        private AudUsdExitManager _audUsdExitManager;
        private IGate _audUsdSessionGate;
        private IGate _audUsdImpulseGate;

        // AUDNZD
        private AudNzdEntryLogic _audNzdEntryLogic;
        private AudNzdInstrumentRiskSizer _audNzdRiskSizer;
        private AudNzdInstrumentExecutor _audNzdExecutor;
        private AudNzdExitManager _audNzdExitManager;
        private IGate _audNzdSessionGate;
        private IGate _audNzdImpulseGate;

        // EURJPY
        private EurJpyEntryLogic _eurJpyEntryLogic;
        private EurJpyInstrumentRiskSizer _eurJpyRiskSizer;
        private EurJpyInstrumentExecutor _eurJpyExecutor;
        private EurJpyExitManager _eurJpyExitManager;
        private IGate _eurJpySessionGate;
        private IGate _eurJpyImpulseGate;

        // GBPJPY
        private GbpJpyEntryLogic _gbpJpyEntryLogic;
        private GbpJpyInstrumentRiskSizer _gbpJpyRiskSizer;
        private GbpJpyInstrumentExecutor _gbpJpyExecutor;
        private GbpJpyExitManager _gbpJpyExitManager;
        private IGate _gbpJpySessionGate;
        private IGate _gbpJpyImpulseGate;

        // NZDUSD
        private NzdUsdEntryLogic _nzdUsdEntryLogic;
        private NzdUsdInstrumentRiskSizer _nzdUsdRiskSizer;
        private NzdUsdInstrumentExecutor _nzdUsdExecutor;
        private NzdUsdExitManager _nzdUsdExitManager;
        private IGate _nzdUsdSessionGate;
        private IGate _nzdUsdImpulseGate;

        // USDCAD
        private UsdCadEntryLogic _usdCadEntryLogic;
        private UsdCadInstrumentRiskSizer _usdCadRiskSizer;
        private UsdCadInstrumentExecutor _usdCadExecutor;
        private UsdCadExitManager _usdCadExitManager;
        private IGate _usdCadSessionGate;
        private IGate _usdCadImpulseGate;

        // USDCHF
        private UsdChfEntryLogic _usdChfEntryLogic;
        private UsdChfInstrumentRiskSizer _usdChfRiskSizer;
        private UsdChfInstrumentExecutor _usdChfExecutor;
        private UsdChfExitManager _usdChfExitManager;
        private IGate _usdChfSessionGate;
        private IGate _usdChfImpulseGate;

        // BTC
        private BtcUsdEntryLogic _btcUsdEntryLogic;
        private BtcUsdInstrumentRiskSizer _btcUsdRiskSizer;
        private BtcUsdInstrumentExecutor _btcUsdExecutor;
        private BtcUsdExitManager _btcUsdExitManager;
        private IGate _btcUsdSessionGate;
        private IGate _btcUsdImpulseGate;

        // ETH
        private EthUsdEntryLogic _ethUsdEntryLogic;
        private EthUsdInstrumentRiskSizer _ethUsdRiskSizer;
        private EthUsdInstrumentExecutor _ethUsdExecutor;
        private EthUsdExitManager _ethUsdExitManager;
        private IGate _ethUsdSessionGate;
        private IGate _ethUsdImpulseGate;
        // ===== Market State Detectors =====

        // =========================
        // FX
        // =========================
        private FxMarketStateDetector _fxMarketStateDetector;
        private FxHtfBiasEngine _fxBias;

        // =========================
        // CRYPTO
        // =========================
        private CryptoMarketStateDetector _cryptoMarketStateDetector;
        private CryptoHtfBiasEngine _cryptoBias;

        // =========================
        // METAL (XAU)
        // =========================
        private XauMarketStateDetector _xauMarketStateDetector;
        private MetalHtfBiasEngine _metalBias;

        // =========================
        // INDEX
        // =========================
        private IndexMarketStateDetector _indexMarketStateDetector;
        private IndexHtfBiasEngine _indexBias;

        private GlobalSessionGate _globalSessionGate;
        private SessionMatrix _sessionMatrix;
        private readonly GeminiRiskConfig _riskConfig;
        private readonly GlobalRiskGuard _globalRiskGuard;

        private EntryContext _ctx;
        private long _entryRouterPassCounter;
        private readonly ContextRegistry _contextRegistry = new ContextRegistry();
        private DateTime _lastContextPruneUtc = DateTime.MinValue;
        private static readonly TimeSpan ContextPruneInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ContextMaxAge = TimeSpan.FromMinutes(30);
        private bool _isMemoryReady;
        private bool _startupCoverageLogged;
        private const bool DebugStartupTrace = false;
        private const int CryptoSurvivableScoreFloor = 20;

        public TradeCore(Robot bot)
        {
            _bot = bot;
            _router = new TradeRouter(_bot);
            _symbolCanonical = SymbolRouting.NormalizeSymbol(_bot.SymbolName);
            _instrumentClass = SymbolRouting.ResolveInstrumentClass(_symbolCanonical);
            _tradeMemoryStore = new TradeMemoryStore();
            _memoryLogger = new MemoryLogger(_bot);
            var symbol = _symbolCanonical;

            // =========================
            // INSTRUMENT ROUTING (SSOT)
            // =========================

            if (_instrumentClass == InstrumentClass.FX)
            {
                _fxMarketStateDetector = new FxMarketStateDetector(_bot, symbol);
                _fxBias = new FxHtfBiasEngine(_bot);
            }
            else if (_instrumentClass == InstrumentClass.CRYPTO)
            {
                _cryptoMarketStateDetector = new CryptoMarketStateDetector(_bot);
                _cryptoBias = new CryptoHtfBiasEngine(_bot);
            }
            else if (_instrumentClass == InstrumentClass.METAL)
            {
                _xauMarketStateDetector = new XauMarketStateDetector(_bot);
                _metalBias = new MetalHtfBiasEngine(_bot);
            }
            else if (_instrumentClass == InstrumentClass.INDEX)
            {
                _indexMarketStateDetector = new IndexMarketStateDetector(_bot);
                _indexBias = new IndexHtfBiasEngine(_bot);
            }

            else
            {
                GlobalLogger.Log(_bot, $"❌ UNKNOWN SYMBOL ROUTING: {symbol}");
            }


            if (_instrumentClass == InstrumentClass.CRYPTO)
            {
                _entryTypes = new List<IEntryType>
                {
                    new BTC_PullbackEntry(),
                    new BTC_FlagEntry(),
                    new BTC_RangeBreakoutEntry()
                };
            }

            else if (_instrumentClass == InstrumentClass.METAL)
            {
                _entryTypes = new List<IEntryType>
                {
                    new XAU_FlagEntry(),
                    new XAU_PullbackEntry(),
                    new XAU_ReversalEntry(),
                    new XAU_ImpulseEntry()
                };
            }
            else if (_instrumentClass == InstrumentClass.FX)
            {
                _entryTypes = new List<IEntryType>
                {
                new FX_FlagEntry(),
                new FX_FlagContinuationEntry(),
                new FX_MicroStructureEntry(),
                new FX_MicroContinuationEntry(),
                new FX_ImpulseContinuationEntry(),   // ← ide
                new FX_PullbackEntry(),
                new FX_RangeBreakoutEntry(),
                new FX_ReversalEntry()
            };
            }
            else if (_instrumentClass == InstrumentClass.INDEX)
            {
                _entryTypes = new List<IEntryType>
                {
                    new Index_PullbackEntry(),
                    new Index_BreakoutEntry(),
                    new Index_FlagEntry()
                };
            }

            else
            {
                GlobalLogger.Log(_bot, $"[WARN] Unknown symbol fallback used: {symbol}");

                _entryTypes = new List<IEntryType>
                {
                    new TC_PullbackEntry(),
                    new TC_FlagEntry(),
                    new BR_RangeBreakoutEntry(),
                    new TR_ReversalEntry(),
                };
            }
        

            _entryRouter = new EntryRouter(_entryTypes);
            _transitionDetector = new TransitionDetector();
            Action<string> safePrint = msg => _bot.BeginInvokeOnMainThread(() => GlobalLogger.Log(_bot, msg));
            _flagBreakoutDetector = new FlagBreakoutDetector(safePrint);
            _logWriter = new LogWriter(safePrint);
            _logger = new CompositeTradeLogger(
                new CsvTradeLogger(_logWriter, safePrint));
            // SSOT ENFORCEMENT: analytics handled by UnifiedAnalyticsWriter
            // CsvAnalyticsLogger removed
            _statsTracker = new TradeStatsTracker(safePrint);
            _memoryEngine = new MarketMemoryEngine(safePrint);
            _contextBuilder = new EntryContextBuilder(bot, _memoryEngine);
            _globalSessionGate = new GlobalSessionGate(_bot);
            _sessionMatrix = new SessionMatrix(new SessionMatrixProvider());
            _riskConfig = new GeminiRiskConfig();
            _globalRiskGuard = new GlobalRiskGuard(_riskConfig, msg => GlobalLogger.Log(_bot, msg));

            _xauEntryLogic = new XauEntryLogic(_bot);
            _xauSessionGate = new XauSessionGate(_bot);
            _xauImpulseGate = new XauImpulseGate(_bot);
            _xauExitManager = new XauExitManager(_bot);
            _xauMarketStateDetector = new XauMarketStateDetector(_bot);
            _xauExecutor = new XauInstrumentExecutor(
                _bot,
                new XauInstrumentRiskSizer(),
                _xauExitManager,
                _positionContexts,
                BotLabel);

            _nasEntryLogic = new NasEntryLogic(_bot);
            _nasRiskSizer = new NasInstrumentRiskSizer();
            _nasExitManager = new NasExitManager(_bot);
            _nasSessionGate = new NasSessionGate(_bot);
            _nasImpulseGate = new NasImpulseGate(_bot);
            _nasExecutor = new NasInstrumentExecutor(
                _bot,
                _nasEntryLogic,
                _nasRiskSizer,
                _nasExitManager,
                _indexMarketStateDetector,
                _positionContexts,
                BotLabel            // ← EZ HIÁNYZOTT
            );


            _us30EntryLogic = new Us30EntryLogic(_bot);
            _us30RiskSizer = new Us30InstrumentRiskSizer();
            _us30ExitManager = new Us30ExitManager(_bot);
            _us30SessionGate = new Us30SessionGate(_bot);
            _us30ImpulseGate = new Us30ImpulseGate(_bot);
            _us30Executor = new Us30InstrumentExecutor(
                _bot,
                _us30EntryLogic,
                _us30RiskSizer,
                _us30ExitManager,
                _indexMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _ger40EntryLogic = new Ger40EntryLogic(_bot);
            _ger40RiskSizer = new Ger40InstrumentRiskSizer();
            _ger40ExitManager = new Ger40ExitManager(_bot);
            _ger40SessionGate = new Ger40SessionGate(_bot);
            _ger40ImpulseGate = new Ger40ImpulseGate(_bot);
            _ger40Executor = new Ger40InstrumentExecutor(
                _bot,
                _ger40EntryLogic,
                _ger40RiskSizer,
                _ger40ExitManager,
                _indexMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _eurUsdEntryLogic = new EurUsdEntryLogic(_bot);
            _eurUsdRiskSizer = new EurUsdInstrumentRiskSizer();
            _eurUsdExitManager = new EurUsdExitManager(_bot);
            _eurUsdSessionGate = new EurUsdSessionGate(_bot);
            _eurUsdImpulseGate = new EurUsdImpulseGate(_bot);
            _eurUsdExecutor = new EurUsdInstrumentExecutor(
                _bot,
                _eurUsdEntryLogic,
                _eurUsdRiskSizer,
                _eurUsdExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _usdJpyEntryLogic = new UsdJpyEntryLogic(_bot);
            _usdJpyRiskSizer = new UsdJpyInstrumentRiskSizer();
            _usdJpyExitManager = new UsdJpyExitManager(_bot);
            _usdJpySessionGate = new UsdJpySessionGate(_bot);
            _usdJpyImpulseGate = new UsdJpyImpulseGate(_bot);
            _usdJpyExecutor = new UsdJpyInstrumentExecutor(
                _bot,
                _usdJpyEntryLogic,
                _usdJpyRiskSizer,       // ← FONTOS
                _usdJpyExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _gbpUsdEntryLogic = new GbpUsdEntryLogic(_bot);
            _gbpUsdRiskSizer = new GbpUsdInstrumentRiskSizer();
            _gbpUsdExitManager = new GbpUsdExitManager(_bot);
            _gbpUsdSessionGate = new GbpUsdSessionGate(_bot);
            _gbpUsdImpulseGate = new GbpUsdImpulseGate(_bot);
            _gbpUsdExecutor = new GbpUsdInstrumentExecutor(
                _bot,
                _gbpUsdEntryLogic,
                _gbpUsdRiskSizer,                 // ← NEM _gbpUsdRiskSizer
                _gbpUsdExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _audUsdEntryLogic = new AudUsdEntryLogic(_bot);
            _audUsdRiskSizer = new AudUsdInstrumentRiskSizer();
            _audUsdExitManager = new AudUsdExitManager(_bot);
            _audUsdSessionGate = new AudUsdSessionGate(_bot);
            _audUsdImpulseGate = new AudUsdImpulseGate(_bot);

            _audUsdExecutor = new AudUsdInstrumentExecutor(
                _bot,
                _audUsdEntryLogic,
                _audUsdRiskSizer,
                _audUsdExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _audNzdEntryLogic = new AudNzdEntryLogic(_bot);
            _audNzdRiskSizer = new AudNzdInstrumentRiskSizer();
            _audNzdExitManager = new AudNzdExitManager(_bot);
            _audNzdSessionGate = new AudNzdSessionGate(_bot);
            _audNzdImpulseGate = new AudNzdImpulseGate(_bot);

            _audNzdExecutor = new AudNzdInstrumentExecutor(
                _bot,
                _audNzdEntryLogic,
                _audNzdRiskSizer,
                _audNzdExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _eurJpyEntryLogic = new EurJpyEntryLogic(_bot);
            _eurJpyRiskSizer = new EurJpyInstrumentRiskSizer();
            _eurJpyExitManager = new EurJpyExitManager(_bot);
            _eurJpySessionGate = new EurJpySessionGate(_bot);
            _eurJpyImpulseGate = new EurJpyImpulseGate(_bot);

            _eurJpyExecutor = new EurJpyInstrumentExecutor(
                _bot,
                _eurJpyEntryLogic,
                _eurJpyRiskSizer,
                _eurJpyExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _gbpJpyEntryLogic = new GbpJpyEntryLogic(_bot);
            _gbpJpyRiskSizer = new GbpJpyInstrumentRiskSizer();
            _gbpJpyExitManager = new GbpJpyExitManager(_bot);
            _gbpJpySessionGate = new GbpJpySessionGate(_bot);
            _gbpJpyImpulseGate = new GbpJpyImpulseGate(_bot);

            _gbpJpyExecutor = new GbpJpyInstrumentExecutor(
                _bot,
                _gbpJpyEntryLogic,
                _gbpJpyRiskSizer,
                _gbpJpyExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _nzdUsdEntryLogic = new NzdUsdEntryLogic(_bot);
            _nzdUsdRiskSizer = new NzdUsdInstrumentRiskSizer();
            _nzdUsdExitManager = new NzdUsdExitManager(_bot);
            _nzdUsdSessionGate = new NzdUsdSessionGate(_bot);
            _nzdUsdImpulseGate = new NzdUsdImpulseGate(_bot);

            _nzdUsdExecutor = new NzdUsdInstrumentExecutor(
                _bot,
                _nzdUsdEntryLogic,
                _nzdUsdRiskSizer,
                _nzdUsdExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _usdCadEntryLogic = new UsdCadEntryLogic(_bot);
            _usdCadRiskSizer = new UsdCadInstrumentRiskSizer();
            _usdCadExitManager = new UsdCadExitManager(_bot);
            _usdCadSessionGate = new UsdCadSessionGate(_bot);
            _usdCadImpulseGate = new UsdCadImpulseGate(_bot);

            _usdCadExecutor = new UsdCadInstrumentExecutor(
                _bot,
                _usdCadEntryLogic,
                _usdCadRiskSizer,
                _usdCadExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _usdChfEntryLogic = new UsdChfEntryLogic(_bot);
            _usdChfRiskSizer = new UsdChfInstrumentRiskSizer();
            _usdChfExitManager = new UsdChfExitManager(_bot);
            _usdChfSessionGate = new UsdChfSessionGate(_bot);
            _usdChfImpulseGate = new UsdChfImpulseGate(_bot);

            _usdChfExecutor = new UsdChfInstrumentExecutor(
                _bot,
                _usdChfEntryLogic,
                _usdChfRiskSizer,
                _usdChfExitManager,
                _fxMarketStateDetector,
                _positionContexts,
                BotLabel
            );

            _btcUsdRiskSizer = new BtcUsdInstrumentRiskSizer();
            _btcUsdEntryLogic = new BtcUsdEntryLogic(_bot);
            _btcUsdExitManager = new BtcUsdExitManager(_bot);
            _btcUsdSessionGate = new BtcUsdSessionGate(_bot);
            _btcUsdImpulseGate = new BtcUsdImpulseGate(_bot);
            _btcUsdExecutor = new BtcUsdInstrumentExecutor(
                _bot,
                _btcUsdEntryLogic,
                _btcUsdRiskSizer,   // ⬅️ EZ A LÉNYEG
                _btcUsdExitManager,
                _cryptoMarketStateDetector,
                _positionContexts,
                BotLabel);

            _ethUsdRiskSizer = new EthUsdInstrumentRiskSizer();
            _ethUsdEntryLogic = new EthUsdEntryLogic(_bot);
            _ethUsdExitManager = new EthUsdExitManager(_bot);
            _ethUsdSessionGate = new EthUsdSessionGate(_bot);
            _ethUsdImpulseGate = new EthUsdImpulseGate(_bot);
            _ethUsdExecutor = new EthUsdInstrumentExecutor(
                _bot,
                _ethUsdEntryLogic,
                _ethUsdRiskSizer,   // ⬅️ EZ A LÉNYEG
                _ethUsdExitManager,
                _cryptoMarketStateDetector,
                _positionContexts,
                BotLabel);

            RegisterExitManager("XAUUSD", _xauExitManager);
            RegisterExitManager("NAS100", _nasExitManager);
            RegisterExitManager("US30", _us30ExitManager);
            RegisterExitManager("GER40", _ger40ExitManager);
            RegisterExitManager("EURUSD", _eurUsdExitManager);
            RegisterExitManager("USDJPY", _usdJpyExitManager);
            RegisterExitManager("GBPUSD", _gbpUsdExitManager);
            RegisterExitManager("AUDUSD", _audUsdExitManager);
            RegisterExitManager("AUDNZD", _audNzdExitManager);
            RegisterExitManager("EURJPY", _eurJpyExitManager);
            RegisterExitManager("GBPJPY", _gbpJpyExitManager);
            RegisterExitManager("NZDUSD", _nzdUsdExitManager);
            RegisterExitManager("USDCAD", _usdCadExitManager);
            RegisterExitManager("USDCHF", _usdChfExitManager);
            RegisterExitManager("BTCUSD", _btcUsdExitManager);
            RegisterExitManager("ETHUSD", _ethUsdExitManager);

            _bot.Positions.Opened += args =>
            {
                var pos = args.Position;
                if (pos == null) return;

                // 🔒 csak saját bot
                if (pos.Label != BotLabel) return;

                // 🔒 csak saját symbol
                if (!pos.SymbolName.Equals(_bot.SymbolName, StringComparison.OrdinalIgnoreCase))
                    return;

                bool bound = _tradeMetaStore.BindToPosition(
                    pos.Id,
                    pos.SymbolName
                );

                GlobalLogger.Log(_bot, bound
                    ? $"[META BIND OK] pos={pos.Id} symbol={pos.SymbolName}"
                    : $"[META BIND FAIL] pos={pos.Id} symbol={pos.SymbolName} (NO PENDING)"
                );

                if (_ctx != null)
                {
                    _contextRegistry.RegisterEntry(pos.Id, _ctx);
                    _statsTracker.RegisterTradeOpen(_ctx, pos.Id);
                }

                _entryBalanceByPositionId[pos.Id] = _bot.Account.Balance;

                if (_positionContexts.TryGetValue(pos.Id, out var pctx))
                {
                    TradeDirection authoritativeFinalDirection = TradeDirection.None;
                    if (_ctx != null && _ctx.FinalDirection != TradeDirection.None)
                    {
                        authoritativeFinalDirection = _ctx.FinalDirection;
                        GlobalLogger.Log(_bot, $"[DIR][SOURCE] posId={pctx.PositionId} source=ctx finalDir={authoritativeFinalDirection}");
                    }
                    // Temporary safety gate: non-rehydrated contexts are treated as higher-trust provenance here.
                    // This is not a final provenance model.
                    else if (!pctx.IsRehydrated && pctx.FinalDirection != TradeDirection.None)
                    {
                        authoritativeFinalDirection = pctx.FinalDirection;
                        GlobalLogger.Log(_bot,
                            $"[DIR][POS_CTX_WARNING] Using existing PositionContext FinalDirection posId={pctx.PositionId} sym={pctx.Symbol} ctxPresent={(_ctx != null).ToString().ToLowerInvariant()}");
                        GlobalLogger.Log(_bot, $"[DIR][SOURCE] posId={pctx.PositionId} source=pctx finalDir={authoritativeFinalDirection} provenanceGate=temporary_non_rehydrated_only");
                    }

                    if (authoritativeFinalDirection == TradeDirection.None)
                    {
                        GlobalLogger.Log(_bot,
                            $"[DIR][POS_CTX_ERROR] Missing authoritative FinalDirection posId={pctx.PositionId} sym={pctx.Symbol} ctxPresent={(_ctx != null).ToString().ToLowerInvariant()} pctxFinal={pctx.FinalDirection} pctxIsRehydrated={pctx.IsRehydrated.ToString().ToLowerInvariant()}");
                        return;
                    }

                    pctx.FinalDirection = authoritativeFinalDirection;
                    GlobalLogger.Log(_bot, $"[DIR][SET] posId={pctx.PositionId} finalDir={pctx.FinalDirection}");

                    if (pctx.FinalDirection == TradeDirection.None)
                    {
                        GlobalLogger.Log(_bot, $"[DIR][POS_CTX_ERROR] Missing FinalDirection posId={pctx.PositionId} sym={pctx.Symbol}");
                        return;
                    }

                    if (_ctx != null && pctx.FinalDirection != _ctx.FinalDirection)
                    {
                        GlobalLogger.Log(_bot, $"[DIR][FATAL_MISMATCH] sym={_bot.SymbolName} stage=position posId={pctx.PositionId} posFinal={pctx.FinalDirection} entryFinal={_ctx.FinalDirection}");
                        return;
                    }

                    _contextRegistry.RegisterPosition(pctx);
                    GlobalLogger.Log(_bot, $"[DIR][POS_CTX] posId={pctx.PositionId} sym={pctx.Symbol} finalDir={pctx.FinalDirection}");
                }

                _tradeMetaStore.TryGet(pos.Id, out var pendingMeta);
                string openedEntryType = pctx?.EntryType ?? pendingMeta?.EntryType ?? "UNKNOWN";
                GlobalLogger.Log(_bot, $"[POSITION][OPEN] symbol={pos.SymbolName ?? _bot.SymbolName} entryType={openedEntryType} positionId={pos.Id} pipelineId={pos.Id}");
                LogScalingOpenAudit(pos, pctx);
                _logger.OnTradeOpened(BuildLogContext(pos, pendingMeta, pctx: _positionContexts.TryGetValue(pos.Id, out var ctxValue) ? ctxValue : null));
            };

            _bot.Positions.Closed += OnPositionClosed;
        }


        public void Init()
        {
            EnsureRuntimeResolverInitialized();
        }

        private void EnsureRuntimeResolverInitialized()
        {
            if (_runtimeSymbols != null)
                return;

            _runtimeSymbols = new RuntimeSymbolResolver(_bot);
            GlobalLogger.Log(_bot, "[RESOLVER][INIT] mode=runtime_only phase=OnStart");
        }

        public void OnBar()
        {
            EnsureRuntimeResolverInitialized();
            _runtimeSymbols.BeginExecutionCycle();
            string rawSym = _bot.SymbolName;
            string sym = NormalizeSymbol(rawSym);   // ✅ CANONICAL

            GlobalLogger.Log(_bot, $"[ONBAR DBG] raw={rawSym} canonical={sym}");

            EnsureStartupMemoryReady();

            bool isFx = _fxMarketStateDetector != null && SymbolRouting.ResolveInstrumentClass(sym) == InstrumentClass.FX;

            bool isCrypto = _cryptoMarketStateDetector != null && SymbolRouting.ResolveInstrumentClass(sym) == InstrumentClass.CRYPTO;

            bool isMetal = _xauMarketStateDetector != null && SymbolRouting.ResolveInstrumentClass(sym) == InstrumentClass.METAL;

            bool isIndex = _indexMarketStateDetector != null && SymbolRouting.ResolveInstrumentClass(sym) == InstrumentClass.INDEX;

            // =========================
            // ADDED: instrument classification (SSOT for this method)
            // =========================
            string symU = sym.ToUpperInvariant();   // ✅ már canonical
            bool isMetalSymbol = SymbolRouting.ResolveInstrumentClass(symU) == InstrumentClass.METAL;
            bool isCryptoSymbol = SymbolRouting.ResolveInstrumentClass(symU) == InstrumentClass.CRYPTO;
            bool isIndexSymbol = SymbolRouting.ResolveInstrumentClass(symU) == InstrumentClass.INDEX;

            bool isFxSymbol =
                !isMetalSymbol && !isCryptoSymbol && !isIndexSymbol &&
                GeminiV26.Instruments.FX.FxInstrumentMatrix.Contains(symU);

            FxMarketState fxState = null;
            XauMarketState xauState = null;
            CryptoMarketState cryptoState = null;
            IndexMarketState indexState = null;

            // =========================
            // MARKET STATE SNAPSHOT
            // =========================
            if (isFx)
            {
                fxState = _fxMarketStateDetector.Evaluate();
                if (fxState != null)
                    GlobalLogger.Log(_bot, $"[FX MarketState] {rawSym} Trend={fxState.IsTrend} Momentum={fxState.IsMomentum} LowVol={fxState.IsLowVol} ADX={fxState.Adx:F1}");
            }
            else if (isCrypto)
            {
                cryptoState = _cryptoMarketStateDetector.Evaluate();
                if (cryptoState != null)
                    GlobalLogger.Log(_bot, $"[CRYPTO MarketState] {rawSym} Trend={cryptoState.IsTrend} Momentum={cryptoState.IsMomentum} LowVol={cryptoState.IsLowVol} ADX={cryptoState.Adx:F1}");
            }
            else if (isMetal)
            {
                xauState = _xauMarketStateDetector.Evaluate();
                if (xauState != null)
                    GlobalLogger.Log(_bot, $"[XAU MarketState] {rawSym} Range={xauState.IsRange} Trend={xauState.IsTrend} Momentum={xauState.IsMomentum} ADX={xauState.Adx:F1} HardRange={xauState.IsHardRange}");
            }
            else if (isIndex)
            {
                indexState = _indexMarketStateDetector.Evaluate();
                if (indexState != null)
                    GlobalLogger.Log(_bot, $"[INDEX MarketState] {rawSym} Trend={indexState.IsTrend} Momentum={indexState.IsMomentum} LowVol={indexState.IsLowVol} ADX={indexState.Adx:F1}");
            }
            else
            {
                GlobalLogger.Log(_bot, $"[TC] WARN: Unknown instrument type in OnBar sym={rawSym}");
            }

            // =========================
            // ADDED: prevent NRE if contexts dictionary isn't initialized yet
            // =========================
            if (_positionContexts == null)
            {
                GlobalLogger.Log(_bot, "BLOCK: _positionContexts == null");
                GlobalLogger.Log(_bot, "[TC] WARN: _positionContexts is NULL (skip exit+entry pipeline this bar)");
                return;
            }

            // =========================
            // Exit management (OnBar)
            // =========================
            foreach (var pos in _bot.Positions)
            {
                if (pos.SymbolName != _bot.SymbolName)
                    continue;

                GlobalLogger.Log(_bot, $"[EXIT DBG] posId={pos.Id} sym={pos.SymbolName}");

                // ⛔ TEMP SAFETY (you already had this)
                if (!_positionContexts.TryGetValue(pos.Id, out var ctx))
                {
                    GlobalLogger.Log(_bot, $"[TC] Context missing for position posId={pos.Id}");
                    GlobalLogger.Log(_bot, $"[REHYDRATE_WARN] pos={Convert.ToInt64(pos.Id)} symbol={pos.SymbolName} reason=exit_pipeline_missing_context");
                    continue;
                }

                ctx.LastUpdateUtc = DateTime.UtcNow;
                _contextRegistry.RegisterPosition(ctx);
                _tradeMetaStore.TryGet(pos.Id, out var openMeta);
                _logger.OnTradeUpdated(BuildLogContext(pos, openMeta, ctx));

                if (IsSymbol("XAUUSD"))
                    _xauExitManager?.OnBar(pos);

                else if (IsNasSymbol(_bot.SymbolName))
                    _nasExitManager?.OnBar(pos);

                else if (IsSymbol("US30"))
                    _us30ExitManager?.OnBar(pos);

                else if (IsSymbol("EURUSD"))
                    _eurUsdExitManager?.OnBar(pos);

                else if (IsSymbol("USDJPY"))
                    _usdJpyExitManager?.OnBar(pos);

                else if (IsSymbol("GBPUSD"))
                    _gbpUsdExitManager?.OnBar(pos);

                else if (IsSymbol("AUDUSD"))
                    _audUsdExitManager?.OnBar(pos);

                else if (IsSymbol("AUDNZD"))
                    _audNzdExitManager?.OnBar(pos);

                else if (IsSymbol("EURJPY"))
                    _eurJpyExitManager?.OnBar(pos);

                else if (IsSymbol("GBPJPY"))
                    _gbpJpyExitManager?.OnBar(pos);

                else if (IsSymbol("NZDUSD"))
                    _nzdUsdExitManager?.OnBar(pos);

                else if (IsSymbol("USDCAD"))
                    _usdCadExitManager?.OnBar(pos);

                else if (IsSymbol("USDCHF"))
                    _usdChfExitManager?.OnBar(pos);

                else if (IsSymbol("BTCUSD"))
                    _btcUsdExitManager?.OnBar(pos);

                else if (IsSymbol("ETHUSD"))
                    _ethUsdExitManager?.OnBar(pos);

                else if (IsSymbol("GER40"))
                    _ger40ExitManager?.OnBar(pos);
            }

            if (_lastContextPruneUtc == DateTime.MinValue ||
                (_bot.Server.Time - _lastContextPruneUtc) >= ContextPruneInterval)
            {
                _contextRegistry.PruneStale(ContextMaxAge, id =>
                    GlobalLogger.Log(_bot, $"[TC] Pruned stale context: positionId={id}"));
                _lastContextPruneUtc = _bot.Server.Time;
            }

            if (HasOpenGeminiPosition())
            {
                GlobalLogger.Log(_bot, "[DEBUG] HasOpenGeminiPosition = TRUE");
                GlobalLogger.Log(_bot, "BLOCK: existing Gemini position open");
                return;
            }

            // =========================
            // ADDED: hard null guards before building context / routing
            // =========================
            if (_contextBuilder == null)
            {
                GlobalLogger.Log(_bot, "BLOCK: _contextBuilder == null");
                GlobalLogger.Log(_bot, "[TC] ERROR: _contextBuilder is NULL (cannot build entry context)");
                return;
            }
            if (_globalSessionGate == null)
            {
                GlobalLogger.Log(_bot, "BLOCK: _globalSessionGate == null");
                GlobalLogger.Log(_bot, "[TC] ERROR: _globalSessionGate is NULL (cannot gate entries)");
                return;
            }
            if (_entryRouter == null)
            {
                GlobalLogger.Log(_bot, "BLOCK: _entryRouter == null");
                GlobalLogger.Log(_bot, "[TC] ERROR: _entryRouter is NULL (cannot evaluate entries)");
                return;
            }

            _ctx = _contextBuilder.Build(_bot.SymbolName);

            // ADDED: context must be ready
            if (_ctx != null)
                _ctx.Log = _bot.Print;
            
            if (_ctx == null || !_ctx.IsReady)
            {
                GlobalLogger.Log(_bot, "BLOCK: EntryContext not ready");
                GlobalLogger.Log(_bot, "[TC] BLOCKED: EntryContext not ready");
                return;
            }

            _ctx.BarsSinceStart = BotRestartState.BarsSinceStart;
            _ctx.Flags.IsDeadMarketBlocked = false;


            if (isMetalSymbol)
            {
                if (xauState == null && _xauMarketStateDetector != null)
                    xauState = _xauMarketStateDetector.Evaluate();
            }

            _ctx.MarketState = BuildEntryMarketState(fxState, cryptoState, xauState, indexState);
            if (_ctx.MarketState != null)
            {
                GlobalLogger.Log(_bot, string.Format(
                    "[ENTRY MARKETSTATE ASSIGN] sym={0} trend={1} momentum={2} lowVol={3} adx={4:F1} atrPts={5:F2}",
                    rawSym,
                    _ctx.MarketState.IsTrend,
                    _ctx.MarketState.IsMomentum,
                    _ctx.MarketState.IsLowVol,
                    _ctx.MarketState.Adx,
                    _ctx.MarketState.AtrPoints));
            }

            _ctx.LastUpdateUtc = DateTime.UtcNow;
            _contextRegistry.RegisterEntry(_ctx);
            _contextRegistry.RebuildFromActivePositions(_bot.Positions, _positionContexts);

            var transition = _transitionDetector.Evaluate(_ctx);
            _ctx.Transition = transition;
            _ctx.TransitionValid = transition.IsValid;
            _ctx.TransitionScoreBonus = transition.BonusScore;
            _flagBreakoutDetector.Evaluate(_ctx);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][CTX_BUILD] sym={_bot.SymbolName} trend={_ctx.TrendDirection} impulse={_ctx.ImpulseDirection} breakout={_ctx.BreakoutDirection} reversal={_ctx.ReversalDirection}", _ctx));

            DateTime utcNow = _bot.Server.Time.ToUniversalTime();
            _bot.Print($"[SESSION][CHECK] utc={utcNow:HH:mm}");
            bool isHardBlocked = _globalSessionGate.IsBlockedSession(utcNow);
            _globalSessionGate.RecordHardBlock(isHardBlocked);
            _bot.Print($"[SESSION][HARD_BLOCK] active={isHardBlocked.ToString().ToLowerInvariant()}");
            _bot.Print($"[SESSION][STATS] blockedCount={_globalSessionGate.BlockedCount} allowedCount={_globalSessionGate.AllowedCount}");
            if (isHardBlocked)
            {
                _bot.Print($"[SESSION][HARD_BLOCK] Trading disabled | UTC={utcNow:HH:mm}");
                GlobalLogger.Log(_bot, "BLOCK: global hard session block");
                return;
            }

            _bot.Print("[SESSION][PASS] allowed=true");

            // =========================
            // GLOBAL SESSION GATE + SESSION MATRIX
            // =========================
            SessionDecision sessionDecision = _globalSessionGate.GetDecision(_bot.SymbolName, _bot.TimeFrame);
            GlobalLogger.Log(_bot, $"[SESSION CHECK] time={_bot.Server.Time:O} symbol={_bot.SymbolName} bucket={sessionDecision.Bucket} allow={sessionDecision.Allow}");
            if (!sessionDecision.Allow)
            {
                GlobalLogger.Log(_bot, "BLOCK: session gate");
                GlobalLogger.Log(_bot, "[TC] BLOCKED: Global SessionGate");
                return;
            }

            string instrumentClass = ResolveInstrumentClass(symU);
            SessionMatrixConfig sessionCfg = _sessionMatrix.Resolve(sessionDecision, instrumentClass, _bot.TimeFrame);
            _ctx.SessionMatrixConfig = sessionCfg;

            GlobalLogger.Log(_bot, string.Format("[SESSION_MATRIX] symbol={0} bucket={1} tier={2} flag={3} breakout={4} pullback={5} minADX={6:F1} minAtrMult={7:F2}",
                _bot.SymbolName,
                sessionDecision.Bucket,
                SessionMatrix.DetectTier(_bot.TimeFrame),
                sessionCfg.AllowFlag,
                sessionCfg.AllowBreakout,
                sessionCfg.AllowPullback,
                sessionCfg.MinAdx,
                sessionCfg.MinAtrMultiplier));

            // =========================
            // SESSION INJECT (STRICT FROM GLOBAL GATE BUCKET)
            // =========================
            _ctx.Session = SessionResolver.FromBucket(sessionDecision.Bucket);
            GlobalLogger.Log(_bot, string.Format("[CTX_SESSION_ASSIGN] sessionFromGate={0} sessionAssigned={1}", sessionDecision.Bucket, _ctx.Session));
            SyncMemoryState(_ctx);

            TradeType xauBias = TradeType.Buy;
            int xauBiasConfidence = 0;
            TradeDirection cryptoBias = TradeDirection.None;
            int cryptoLogicConfidence = 0;
            TradeDirection logicBias = TradeDirection.None;
            int logicConfidence = 0;

            if (IsSymbol("XAUUSD"))
            {
                _xauEntryLogic?.Evaluate(out xauBias, out xauBiasConfidence);
                logicBias = FromTradeType(xauBias);
                logicConfidence = xauBiasConfidence;
            }

            if (IsSymbol("EURUSD"))
            {
                _eurUsdEntryLogic?.Evaluate();
                logicBias = FromTradeType(_eurUsdEntryLogic.LastBias);
                logicConfidence = _eurUsdEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("GBPUSD"))
            {
                _gbpUsdEntryLogic?.Evaluate();
                if (_gbpUsdEntryLogic != null && _gbpUsdEntryLogic.CheckEntry(out var gbpBias, out var gbpLogicConfidence))
                {
                    logicBias = gbpBias;
                    logicConfidence = gbpLogicConfidence;
                }
            }

            if (IsSymbol("USDJPY"))
            {
                _usdJpyEntryLogic?.Evaluate();
                logicConfidence = _usdJpyEntryLogic.LastLogicConfidence;
                logicBias = logicConfidence > 0
                    ? FromTradeType(_usdJpyEntryLogic.LastBias)
                    : TradeDirection.None;
            }

            if (IsSymbol("AUDUSD"))
            {
                _audUsdEntryLogic?.Evaluate();
                logicBias = FromTradeType(_audUsdEntryLogic.LastBias);
                logicConfidence = _audUsdEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("AUDNZD"))
            {
                _audNzdEntryLogic?.Evaluate();
                logicBias = FromTradeType(_audNzdEntryLogic.LastBias);
                logicConfidence = _audNzdEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("EURJPY"))
            {
                _eurJpyEntryLogic?.Evaluate();
                logicBias = FromTradeType(_eurJpyEntryLogic.LastBias);
                logicConfidence = _eurJpyEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("GBPJPY"))
            {
                _gbpJpyEntryLogic?.Evaluate();
                logicBias = FromTradeType(_gbpJpyEntryLogic.LastBias);
                logicConfidence = _gbpJpyEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("NZDUSD"))
            {
                _nzdUsdEntryLogic?.Evaluate();
                logicBias = FromTradeType(_nzdUsdEntryLogic.LastBias);
                logicConfidence = _nzdUsdEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("USDCAD"))
            {
                _usdCadEntryLogic?.Evaluate();
                logicBias = FromTradeType(_usdCadEntryLogic.LastBias);
                logicConfidence = _usdCadEntryLogic.LastLogicConfidence;
            }

            if (IsSymbol("USDCHF"))
            {
                _usdChfEntryLogic?.Evaluate();
                logicBias = FromTradeType(_usdChfEntryLogic.LastBias);
                logicConfidence = _usdChfEntryLogic.LastLogicConfidence;
            }

            if (IsNasSymbol(_bot.SymbolName))
            {
                _nasEntryLogic?.Evaluate();
                if (_nasEntryLogic != null)
                {
                    logicBias = FromTradeType(_nasEntryLogic.LastBias);
                    logicConfidence = _nasEntryLogic.LastLogicConfidence;
                }
            }

            if (IsSymbol("GER40"))
            {
                _ger40EntryLogic?.Evaluate();
                if (_ger40EntryLogic != null)
                {
                    logicBias = FromTradeType(_ger40EntryLogic.LastBias);
                    logicConfidence = _ger40EntryLogic.LastLogicConfidence;
                }
            }

            if (IsSymbol("US30"))
            {
                if (_us30EntryLogic != null && _us30EntryLogic.CheckEntry(out var us30Bias, out var us30LogicConfidence))
                {
                    logicBias = us30Bias;
                    logicConfidence = us30LogicConfidence;
                }
            }

            if (IsSymbol("BTCUSD"))
            {
                _btcUsdEntryLogic?.Evaluate(out cryptoBias, out cryptoLogicConfidence);
                logicBias = cryptoBias;
                logicConfidence = cryptoLogicConfidence;
            }

            if (IsSymbol("ETHUSD"))
            {
                _ethUsdEntryLogic?.Evaluate(out cryptoBias, out cryptoLogicConfidence);
                logicBias = cryptoBias;
                logicConfidence = cryptoLogicConfidence;
            }

            _ctx.LogicBiasDirection = logicBias;
            _ctx.LogicBiasConfidence = logicConfidence;

            if (_ctx.LogicBiasDirection == TradeDirection.None && _ctx.TrendDirection != TradeDirection.None)
            {
                _ctx.LogicBiasDirection = _ctx.TrendDirection;
                _ctx.LogicBiasConfidence = 50;
                GlobalLogger.Log(_bot, "[BIAS FALLBACK] using TrendDirection");
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][LOGIC] sym={_bot.SymbolName} logicBias={_ctx.LogicBiasDirection} logicConf={_ctx.LogicBiasConfidence}", _ctx));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[CTX PROPAGATION] symbol={_bot.SymbolName} bias={_ctx.LogicBias} conf={_ctx.LogicConfidence}", _ctx));

            if (IsSymbol("AUDNZD"))
                GlobalLogger.Log(_bot, $"[AUDNZD TRACE] step2_ctx={_ctx.LogicBiasDirection} conf={_ctx.LogicBiasConfidence}");

            GlobalLogger.Log(_bot, $"[DEBUG] HasOpenGeminiPosition={HasOpenGeminiPosition()}");
            GlobalLogger.Log(_bot, $"[DEBUG] M5.Count={_ctx?.M5?.Count}");

            int minBars = IsSymbol("EURUSD") ? 10 : 30;
            if (_ctx?.M5 == null || _ctx.M5.Count < minBars) return;

            _entryRouterPassCounter++;
            _ctx.DirectionDebugLogged = false;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[PIPE][ENTRY_ROUTER_PASS] pass={_entryRouterPassCounter} symbol={_bot.SymbolName} bar={_bot.Server.Time:O}", _ctx));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[ENTRY START] symbol={_bot.SymbolName} bias={_ctx.LogicBias}", _ctx));

            var signals = _entryRouter.Evaluate(new[] { _ctx });

            GlobalLogger.Log(_bot, 
                $"[PIPE] symbol={_bot.SymbolName} " +
                $"hasSignals={signals.ContainsKey(_bot.SymbolName)} " +
                $"count={(signals.ContainsKey(_bot.SymbolName) ? signals[_bot.SymbolName].Count : -1)}"
            );

            GlobalLogger.Log($"[DEBUG] signals.Keys = {string.Join(",", signals.Keys)}");

            if (!signals.TryGetValue(_bot.SymbolName, out var symbolSignals))
            {
                GlobalLogger.Log(_bot, "[DEBUG] NO signals for symbol");
                return;
            }

            int countBefore = symbolSignals?.Count ?? 0;
            LogIndexFunnelStage(_ctx, symbolSignals, "BUILT");
            if (isMetalSymbol)
                GlobalLogger.Log(_bot, $"[TC][XAU] candidates BEFORE filter: {countBefore}");

            ApplyTransitionScoreBoost(_ctx, symbolSignals);

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DBG ENTRY] total candidates={symbolSignals.Count}", _ctx));
            var fxCreatedScores = new Dictionary<EntryEvaluation, int>();
            var fxPreviousValidity = new Dictionary<EntryEvaluation, bool>();
            int fxCreatedCount = 0;
            int fxValidAfterMatrixCount = 0;
            int fxValidFinalCount = 0;

            foreach (var e in symbolSignals)
            {
                StampEntrySourceHtfTrace(_ctx, e);
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][ROUTER_CAND] sym={_bot.SymbolName} type={e?.Type} valid={e?.IsValid} score={e?.Score} dir={e?.Direction} reason={e?.Reason}", _ctx));
                if (isFxSymbol && IsFxCandidate(e))
                {
                    fxCreatedCount++;
                    fxCreatedScores[e] = e.Score;
                    fxPreviousValidity[e] = e.IsValid;
                    LogFxEntryPipelineStage(_ctx, e, "CREATED");
                }
                LogHtfFlowStage(_ctx, e, "ENTRY_EVALUATION", "_entryRouter.Evaluate");
                if (e != null)
                {
                    EnsureHtfClassification(_ctx, e);
                    e.AfterHtfScoreAdjustment = e.Score;
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY_TRACE][LOGIC] symbol={e.Symbol ?? _bot.SymbolName} entryType={e.Type} stage=LOGIC candidateDirection={GetEntryTraceCandidateDirection(e)} score={e.Score} classification={e.HtfClassification} " +
                        $"rawDirection={e.RawDirection} logicBiasDirection={e.LogicBiasDirection} logicConfidence={e.RawLogicConfidence} patternDetected={e.PatternDetected.ToString().ToLowerInvariant()} setupType={e.SetupType ?? e.Type.ToString()}",
                        _ctx));
                }
            }

        // =====================================================
        // HTF BIAS = SCORE-ONLY CONTEXT (Asset-group level)
        // Handles FX / Crypto / Metals / Index policies without filtering
        // =====================================================

        if (isFxSymbol)
        {
            var bias = BuildHtfSnapshotFromContext(_ctx, InstrumentClass.FX);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "FX");
        }
        else if (isCryptoSymbol)
        {
            var bias = BuildHtfSnapshotFromContext(_ctx, InstrumentClass.CRYPTO);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "CRYPTO");
        }
        else if (isMetalSymbol)
        {
            var bias = BuildHtfSnapshotFromContext(_ctx, InstrumentClass.METAL);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "XAU");
        }
        else if (isIndexSymbol)
        {
            var bias = BuildHtfSnapshotFromContext(_ctx, InstrumentClass.INDEX);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "INDEX");
        }
                foreach (var e in symbolSignals)
                {
                    if (isFxSymbol && IsFxCandidate(e))
                    {
                        bool wasValidBeforeStage = fxPreviousValidity.TryGetValue(e, out var prevValid) ? prevValid : e.IsValid;
                        LogFxEntryPipelineStage(_ctx, e, "AFTER_MATRIX");
                        if (e.IsValid)
                            fxValidAfterMatrixCount++;
                        LogFxRejectTransition(_ctx, e, "Matrix", wasValidBeforeStage);
                        fxPreviousValidity[e] = e.IsValid;
                    }
                    LogHtfFlowStage(_ctx, e, "ENTRY_FILTER", nameof(ApplyHtfBiasScoreOnly));
                }

                UpdateExecutionStateMachine(_ctx, symbolSignals);
                LogIndexFunnelStage(_ctx, symbolSignals, "AFTER_TRANSITION");
                if (isFxSymbol)
                {
                    foreach (var e in symbolSignals.Where(x => IsFxCandidate(x)))
                    {
                        bool wasValidBeforeStage = fxPreviousValidity.TryGetValue(e, out var prevValid) ? prevValid : e.IsValid;
                        LogFxEntryPipelineStage(_ctx, e, "AFTER_STRUCTURE");
                        LogFxRejectTransition(_ctx, e, "Structure", wasValidBeforeStage);
                        fxPreviousValidity[e] = e.IsValid;
                    }
                }
                ApplyRestartProtection(_ctx, symbolSignals);
                LogIndexFunnelStage(_ctx, symbolSignals, "AFTER_FLAG");
                if (isFxSymbol)
                {
                    foreach (var e in symbolSignals.Where(x => IsFxCandidate(x)))
                    {
                        bool wasValidBeforeStage = fxPreviousValidity.TryGetValue(e, out var prevValid) ? prevValid : e.IsValid;
                        LogFxEntryPipelineStage(_ctx, e, "AFTER_REGIME");
                        LogFxRejectTransition(_ctx, e, "Regime", wasValidBeforeStage);
                        fxPreviousValidity[e] = e.IsValid;
                    }

                    const int fxPenaltyCap = 10;
                    foreach (var e in symbolSignals.Where(x => IsFxCandidate(x) && x != null && fxCreatedScores.ContainsKey(x)))
                    {
                        int createdScore = fxCreatedScores[e];
                        int totalPenalty = Math.Max(0, createdScore - e.Score);
                        int cappedPenalty = Math.Min(totalPenalty, fxPenaltyCap);
                        int boundedScore = Math.Max(0, createdScore - cappedPenalty);
                        if (boundedScore != e.Score)
                        {
                            int beforeCap = e.Score;
                            e.Score = boundedScore;
                            e.Reason = string.IsNullOrWhiteSpace(e.Reason)
                                ? "[FX_PENALTY_CAP]"
                                : $"{e.Reason} [FX_PENALTY_CAP]";
                            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                                $"[FX][PENALTY_CAP] symbol={e.Symbol ?? _bot.SymbolName} entryType={e.Type} createdScore={createdScore} score={beforeCap}->{e.Score} totalPenalty={totalPenalty} cap={fxPenaltyCap}",
                                _ctx));
                        }
                    }
                }
                foreach (var e in symbolSignals)
                {
                    if (e != null)
                    {
                        e.AfterPenaltyScore = e.Score;
                        e.FinalScoreSnapshot = e.Score;
                        e.ScoreThresholdSnapshot = EntryDecisionPolicy.MinScoreThreshold;
                        e.DirectionAfterScore = e.Direction;
                        bool passedThreshold = e.Score >= EntryDecisionPolicy.MinScoreThreshold;
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                            $"[ENTRY_TRACE][SCORE] symbol={e.Symbol ?? _bot.SymbolName} entryType={e.Type} stage=SCORE candidateDirection={GetEntryTraceCandidateDirection(e)} score={e.Score} classification={e.HtfClassification} " +
                            $"baseScore={e.BaseScore} afterHtfScoreAdjustment={e.AfterHtfScoreAdjustment} afterPenalty={e.AfterPenaltyScore} finalScore={e.FinalScoreSnapshot} " +
                            $"scoreThreshold={e.ScoreThresholdSnapshot} passedThreshold={passedThreshold.ToString().ToLowerInvariant()}",
                            _ctx));
                    }

                    LogHtfFlowStage(_ctx, e, "ENTRY_FILTER", nameof(ApplyRestartProtection));
                }

                foreach (var e in symbolSignals)
                    LogEntryTraceClassification(_ctx, e);

                foreach (var e in symbolSignals.Where(x => x != null))
                {
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY_TRACE][GATES] symbol={e.Symbol ?? _bot.SymbolName} entryType={e.Type} stage=GATES candidateDirection={GetEntryTraceCandidateDirection(e)} score={e.Score} classification={e.HtfClassification}",
                        _ctx));
                    LogCriticalDirectionDrop(_ctx, e);
                }

                int countAfter = symbolSignals?.Count ?? 0;
                if (isMetalSymbol)
                    GlobalLogger.Log(_bot, $"[TC][XAU] candidates AFTER filter: {countAfter}");

                LogEntryTraceSummary(_ctx, symbolSignals);

                // =====================================================
                // ROUTER
                // =====================================================
                var selected = _router.SelectEntry(symbolSignals, _ctx);
                if (isIndexSymbol)
                    GlobalLogger.Log(_bot, $"[INDEX][FUNNEL] stage=AFTER_ROUTER count={(selected != null ? 1 : 0)}");
                if (isFxSymbol)
                {
                    foreach (var e in symbolSignals.Where(x => IsFxCandidate(x)))
                    {
                        bool wasValidBeforeStage = fxPreviousValidity.TryGetValue(e, out var prevValid) ? prevValid : e.IsValid;
                        LogFxEntryPipelineStage(_ctx, e, "AFTER_ROUTER");
                        LogFxRejectTransition(_ctx, e, "Router", wasValidBeforeStage);
                        fxPreviousValidity[e] = e.IsValid;
                    }
                }
                bool hasWinner = selected != null;
                double topCandidateScore = symbolSignals
                    .Where(x => x != null)
                    .Select(x => (double)x.Score)
                    .DefaultIfEmpty(double.MinValue)
                    .Max();
                var bestCandidate = symbolSignals
                    .Where(x => x != null)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();
                bool fallbackLogged = false;

                foreach (var candidate in symbolSignals.Where(x => x != null))
                {
                    bool isTopRanked = candidate.Score >= topCandidateScore;
                    bool shouldLogCandidate = isTopRanked || candidate.Score >= 50 || !hasWinner;
                    if (!shouldLogCandidate)
                        continue;

                    if (!hasWinner)
                    {
                        if (fallbackLogged || !ReferenceEquals(candidate, bestCandidate))
                            continue;

                        GlobalLogger.Log(_bot, $"[ENTRY][CANDIDATE] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} score={candidate.Score:0.##} confidence={candidate.LogicConfidence:0.##} trend={_ctx?.TrendDirection} fallback=true");
                        fallbackLogged = true;
                        continue;
                    }

                    GlobalLogger.Log(_bot, $"[ENTRY][CANDIDATE] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} score={candidate.Score:0.##} confidence={candidate.LogicConfidence:0.##} trend={_ctx?.TrendDirection}");
                }

                GlobalLogger.Log(_bot, $"[TRACE] selected is null = {selected == null}");
                if (selected != null)
                    LogHtfFlowStage(_ctx, selected, "ROUTER_CONSUME", nameof(TradeRouter.SelectEntry));

                if (selected == null)
                {
                    if (isFxSymbol)
                    {
                        fxValidFinalCount = symbolSignals.Count(x => IsFxCandidate(x) && x != null && x.IsValid);
                        GlobalLogger.Log(_bot, $"[FX][TOTAL_REJECTION] symbol={_bot.SymbolName} candidates={fxCreatedCount} reason=ALL_INVALID");
                        GlobalLogger.Log(_bot, $"[FX][ENTRY_STATS] created={fxCreatedCount} validAfterMatrix={fxValidAfterMatrixCount} validFinal={fxValidFinalCount}");
                    }
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY_TRACE][FINAL] symbol={_bot.SymbolName} entryType=None stage=FINAL candidateDirection={TradeDirection.None} score=NA classification=HTF_NO_DIRECTION " +
                        $"finalCandidateDirection={TradeDirection.None} finalScore=NA blocked=true finalReason=NO_SELECTED_ENTRY",
                        _ctx));
                    GlobalLogger.Log(_bot, "BLOCK: entry gate");
                    GlobalLogger.Log(_bot, "[TC] NO SELECTED ENTRY (all invalid)");
                    return;
                }

                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    $"[ENTRY_TRACE][FINAL] symbol={selected.Symbol ?? _bot.SymbolName} entryType={selected.Type} stage=FINAL candidateDirection={GetEntryTraceCandidateDirection(selected)} score={selected.Score} " +
                    $"classification={selected.HtfClassification} finalCandidateDirection={selected.Direction} finalScore={selected.Score} blocked={(!selected.IsValid).ToString().ToLowerInvariant()} finalReason={selected.Reason ?? "NA"}",
                    _ctx));
                if (isFxSymbol)
                {
                    foreach (var e in symbolSignals.Where(x => IsFxCandidate(x)))
                    {
                        LogFxEntryPipelineStage(_ctx, e, "FINAL");
                        GlobalLogger.Log(_bot,
                            $"[FX][ENTRY_PIPELINE_FINAL] symbol={e.Symbol ?? _bot.SymbolName} entryType={e.Type} isValid={e.IsValid.ToString().ToLowerInvariant()} finalScore={e.Score} finalConfidence={e.LogicConfidence:0.##}");
                        if (e.IsValid)
                            fxValidFinalCount++;
                    }
                    GlobalLogger.Log(_bot, $"[FX][ENTRY_STATS] created={fxCreatedCount} validAfterMatrix={fxValidAfterMatrixCount} validFinal={fxValidFinalCount}");
                }


                // =====================================================
                // XAU HARD RANGE BLOCK
                // =====================================================
                if (IsSymbol("XAUUSD") &&
                    _xauMarketStateDetector != null)
                {
                    xauState = _xauMarketStateDetector.Evaluate();

                    if (xauState != null)
                    {
                        bool isChop = xauState.IsRange && !xauState.IsTrend;

                        bool hasDirectionalImpulse =
                            selected.Direction == TradeDirection.Long
                                ? _ctx.HasImpulseLong_M5
                                : selected.Direction == TradeDirection.Short
                                    ? _ctx.HasImpulseShort_M5
                                    : false;

                        int barsSinceImpulse =
                            selected.Direction == TradeDirection.Long
                                ? _ctx.BarsSinceImpulseLong_M5
                                : selected.Direction == TradeDirection.Short
                                    ? _ctx.BarsSinceImpulseShort_M5
                                    : 999;

                        double transitionQuality =
                            _ctx.Transition?.QualityScore01 > 0
                                ? _ctx.Transition.QualityScore01
                                : _ctx.Transition?.QualityScore ?? 0.0;

                        bool hasRecentImpulse =
                            hasDirectionalImpulse &&
                            transitionQuality >= 0.60 &&
                            barsSinceImpulse <= 5;

                        bool isCompressionStructure =
                            _ctx.Transition != null &&
                            transitionQuality >= 0.50 &&
                            xauState.RangeWidthAtr < 0.50;

                        bool isFlagLike = hasRecentImpulse && isCompressionStructure;

                        GlobalLogger.Log(_bot,
                            $"[XAU][REGIME][STATE] chop={isChop.ToString().ToLowerInvariant()} impulse={hasRecentImpulse.ToString().ToLowerInvariant()} compression={isCompressionStructure.ToString().ToLowerInvariant()}");

                        if (isChop)
                        {
                            if (isFlagLike)
                            {
                                GlobalLogger.Log(_bot,
                                    $"[XAU][REGIME][FLAG_ALLOW] impulse={hasRecentImpulse.ToString().ToLowerInvariant()} compression={isCompressionStructure.ToString().ToLowerInvariant()} tq={transitionQuality:F2}");
                                LogEntryTraceGate(_ctx, selected, "MarketStateGate", selected.Direction, false, "XAU_FLAG_ALLOW");
                            }
                            else
                            {
                                bool noImpulse = !hasRecentImpulse;
                                bool noCompression = !isCompressionStructure;
                                LogEntryTraceGate(_ctx, selected, "MarketStateGate", selected.Direction, true, "XAU_CHOP_NO_FLAG");
                                GlobalLogger.Log(_bot,
                                    $"[XAU][REGIME][CHOP_BLOCK] noImpulse={noImpulse.ToString().ToLowerInvariant()} noCompression={noCompression.ToString().ToLowerInvariant()}");
                                GlobalLogger.Log(_bot,
                                    $"[TC] ENTRY BLOCKED: XAU RANGE REGIME Width={xauState.RangeWidth:F2} ADX={xauState.Adx:F1} ATR={xauState.Atr:F2}");
                                return;
                            }
                        }
                    }

                    LogEntryTraceGate(_ctx, selected, "MarketStateGate", selected.Direction, false, "PASS");
                }

                _ctx.FinalDirection = selected.Direction;


                // =====================================================
                // META STORE GUARD
                // =====================================================
                if (_tradeMetaStore == null)
                {
                    GlobalLogger.Log(_bot, "[TC] ERROR: _tradeMetaStore is NULL (skip entry)");
                    return;
                }


                // =====================================================
                // REGISTER ENTRY META
                // =====================================================
                _tradeMetaStore.RegisterPending(
                    _bot.SymbolName,
                    new PendingEntryMeta
                    {
                        EntryType = selected.Type.ToString(),
                        EntryReason = selected.Reason,
                        EntryScore = Convert.ToInt32(selected.Score)
                    }
                );

                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[TC] ENTRY WINNER {selected.Type} dir={selected.Direction} score={selected.Score}", _ctx));
                if (isIndexSymbol)
                    GlobalLogger.Log(_bot, "[INDEX][FUNNEL] stage=EXECUTED count=1");
                GlobalLogger.Log(_bot, $"[POS ?] [ENTRY] symbol={selected.Symbol ?? _bot.SymbolName} score={selected.Score} direction={selected.Direction}");
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][ROUTED] sym={_bot.SymbolName} type={selected.Type} routedDir={selected.Direction} score={selected.Score}", _ctx));
                GlobalLogger.Log(_bot, $"[ENTRY][WINNER] symbol={selected.Symbol ?? _bot.SymbolName} entryType={selected.Type} positionId=0 pipelineId={_ctx?.TempId} score={selected.Score:0.##} confidence={selected.LogicConfidence:0.##}");
                double finalRawTq = _ctx?.Transition?.QualityScore ?? 0.0;
                double finalTq = _ctx?.Transition?.QualityScore01 ?? 0.0;
                bool structureAligned = _ctx?.QualificationState?.HasStructure == true;
                GlobalLogger.Log(_bot,
                    $"[ENTRY][TQ_TRACE] symbol={selected.Symbol ?? _bot.SymbolName} type={selected.Type} rawTQ={finalRawTq:0.00} tq={finalTq:0.00} thresholds=transition:0.42,momentum:0.47,structure:0.52 structureAligned={structureAligned.ToString().ToLowerInvariant()} decision=pending");

                if (!PassFinalAcceptance(_ctx, selected))
                {
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][TQ_TRACE] symbol={selected.Symbol ?? _bot.SymbolName} type={selected.Type} rawTQ={finalRawTq:0.00} tq={finalTq:0.00} thresholds=transition:0.42,momentum:0.47,structure:0.52 structureAligned={structureAligned.ToString().ToLowerInvariant()} decision=block");
                    LogHtfFlowStage(_ctx, selected, "FINAL_DECISION", nameof(PassFinalAcceptance));
                    GlobalLogger.Log(_bot, "BLOCK: final acceptance gate");
                    return;
                }
                GlobalLogger.Log(_bot,
                    $"[ENTRY][TQ_TRACE] symbol={selected.Symbol ?? _bot.SymbolName} type={selected.Type} rawTQ={finalRawTq:0.00} tq={finalTq:0.00} thresholds=transition:0.42,momentum:0.47,structure:0.52 structureAligned={structureAligned.ToString().ToLowerInvariant()} decision=pass");

                _ctx.RoutedDirection = selected.Direction;
                _ctx.FinalDirection = selected.Direction;
                var logicDir = _ctx.LogicBiasDirection;
                var evalDir = selected.Direction;
                var routedDir = _ctx.RoutedDirection;
                var finalDir = _ctx.FinalDirection;
                GlobalLogger.Log(_bot, $"[DIR] logic={logicDir} eval={evalDir} routed={routedDir} final={finalDir}");
                _ctx.EntryScore = PositionContext.ClampRiskConfidence(selected.Score);
                _ctx.LogicBiasConfidence = PositionContext.ClampRiskConfidence(Math.Max(0, _ctx.LogicBiasConfidence));
                _ctx.FinalConfidence = PositionContext.ComputeFinalConfidenceValue(_ctx.EntryScore, _ctx.LogicBiasConfidence);
                _ctx.RiskConfidence = PositionContext.ClampRiskConfidence(_ctx.FinalConfidence);
                LogHtfFlowStage(_ctx, selected, "FINAL_DECISION", "DirectionSet");
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][SET] sym={_ctx.Symbol} finalDir={_ctx.FinalDirection}", _ctx));

                if (_ctx.FinalDirection == TradeDirection.None)
                {
                    selected.LastDirectionDropStage = "FINAL";
                    selected.LastDirectionDropModule = "DirectionSet";
                    selected.DirectionAfterGates = TradeDirection.None;
                    LogCriticalDirectionDrop(_ctx, selected);
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY_TRACE][FINAL] symbol={selected.Symbol ?? _bot.SymbolName} entryType={selected.Type} stage=FINAL candidateDirection={GetEntryTraceCandidateDirection(selected)} score={selected.Score} classification={selected.HtfClassification} finalCandidateDirection={TradeDirection.None} finalScore={selected.Score} blocked=true finalReason=FINAL_DIRECTION_NONE",
                        _ctx));
                    GlobalLogger.Log(_bot, "BLOCK: direction/entry failed");
                    GlobalLogger.Log(_bot, $"[TC] ENTRY DROPPED: Direction=None (type={selected.Type} score={selected.Score} reason={selected.Reason})");
                    return;
                }

                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][FINAL] sym={_bot.SymbolName} routed={_ctx.RoutedDirection} final={_ctx.FinalDirection}", _ctx));
                DirectionGuard.Validate(_ctx, null, _bot.Print);

                if (!ValidateDirectionConsistency(_ctx, selected))
                {
                    GlobalLogger.Log(_bot, "BLOCK: direction/entry failed");
                    return;
                }

                LogEntrySnapshot(_ctx, selected);
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[HTF][PASS] dir={_ctx.ActiveHtfDirection} conf={_ctx.ActiveHtfConfidence:F2}", _ctx));
                if (_ctx.ActiveHtfDirection == TradeDirection.None)
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId("[HTF][WARN] Missing HTF snapshot", _ctx));
                double requestRiskPercent = ResolveExecutionRiskPercent(selected, _ctx);
                GlobalLogger.Log(_bot, $"[ENTRY][EXEC][REQUEST] symbol={selected.Symbol ?? _bot.SymbolName} entryType={selected.Type} positionId=0 pipelineId={_ctx?.TempId} score={selected.Score:0.##} riskPercent={requestRiskPercent:0.##}");

                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][EXEC_PRE] sym={_bot.SymbolName} finalCtxDir={_ctx.FinalDirection}", _ctx));
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][EXEC_CONFIRMED] sym={_bot.SymbolName} finalDir={_ctx.FinalDirection}", _ctx));

                if (!HasDirectionTraceCompleteness(_ctx))
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId($"[DIR][TRACE_INCOMPLETE] sym={_bot.SymbolName} finalDir={_ctx.FinalDirection}", _ctx));

                var gateDir = ToTradeTypeStrict(_ctx.FinalDirection);
                GlobalLogger.Log(_bot, "CHECK: direction gate");
                GlobalLogger.Log(_bot, "CHECK: entry gate");

                var utcNowRisk = _bot.Server.Time.ToUniversalTime();
                var currentEquity = _bot.Account.Equity;
                if (!_globalRiskGuard.CanTrade(currentEquity, utcNowRisk))
                {
                    GlobalLogger.Log(_bot, "[RISK][DD_BLOCK] Daily DD limit reached");
                    return;
                }

            // === GATES ONLY ===
            if (IsSymbol("XAUUSD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _xauSessionGate?.AllowEntry(gateDir) ?? false, "XAU SessionGate"))
                {
                    GlobalLogger.Log(_bot, "BLOCK: session gate");
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: XAU SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _xauImpulseGate?.AllowEntry(gateDir) ?? false, "XAU ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "BLOCK: direction/entry failed");
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: XAU ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _xauExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsNasSymbol(_bot.SymbolName))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _nasSessionGate?.AllowEntry(gateDir) ?? false, "NAS SessionGate"))
                {
                    GlobalLogger.Log(_bot, "BLOCK: session gate");
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: NAS SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _nasImpulseGate?.AllowEntry(gateDir) ?? false, "NAS ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "BLOCK: direction/entry failed");
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: NAS ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _nasExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsUs30(_bot.SymbolName))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _us30SessionGate.AllowEntry(gateDir), "US30 SessionGate")) return;
                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _us30ImpulseGate.AllowEntry(gateDir), "US30 ImpulseGate")) return;
                LogEntryExecuted(selected);
                _us30Executor.ExecuteEntry(selected, _ctx);
            }
            else if (IsSymbol("GER40"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _ger40SessionGate?.AllowEntry(gateDir) ?? false, "GER40 SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: GER40 SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _ger40ImpulseGate?.AllowEntry(gateDir) ?? false, "GER40 ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: GER40 ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _ger40Executor?.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("EURUSD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _eurUsdSessionGate?.AllowEntry(gateDir) ?? false, "EUR SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: EUR SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _eurUsdImpulseGate?.AllowEntry(gateDir) ?? false, "EUR ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: EUR ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _eurUsdExecutor?.ExecuteEntry(selected, _ctx);
              
            }
            else if (IsSymbol("USDJPY"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _usdJpySessionGate?.AllowEntry(gateDir) ?? false, "USDJPY SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: USDJPY SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _usdJpyImpulseGate?.AllowEntry(gateDir) ?? false, "USDJPY ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: USDJPY ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _usdJpyExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsSymbol("GBPUSD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _gbpUsdSessionGate?.AllowEntry(gateDir) ?? false, "GBPUSD SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: GBPUSD SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _gbpUsdImpulseGate?.AllowEntry(gateDir) ?? false, "GBPUSD ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: GBPUSD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _gbpUsdExecutor?.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("AUDUSD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _audUsdSessionGate.AllowEntry(gateDir), "AUDUSD SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: AUDUSD SessionGate");
                    return;
                }
                
                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _audUsdImpulseGate.AllowEntry(gateDir), "AUDUSD ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: AUDUSD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _audUsdExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("AUDNZD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _audNzdSessionGate.AllowEntry(gateDir), "AUDNZD SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: AUDNZD SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _audNzdImpulseGate.AllowEntry(gateDir), "AUDNZD ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: AUDNZD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _audNzdExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("EURJPY"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _eurJpySessionGate.AllowEntry(gateDir), "EURJPY SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: EURJPY SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _eurJpyImpulseGate.AllowEntry(gateDir), "EURJPY ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: EURJPY ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _eurJpyExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("GBPJPY"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _gbpJpySessionGate.AllowEntry(gateDir), "GBPJPY SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: GBPJPY SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _gbpJpyImpulseGate.AllowEntry(gateDir), "GBPJPY ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: GBPJPY ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _gbpJpyExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("NZDUSD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _nzdUsdSessionGate.AllowEntry(gateDir), "NZDUSD SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: NZDUSD SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _nzdUsdImpulseGate.AllowEntry(gateDir), "NZDUSD ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: NZDUSD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _nzdUsdExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("USDCAD"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _usdCadSessionGate.AllowEntry(gateDir), "USDCAD SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: USDCAD SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _usdCadImpulseGate.AllowEntry(gateDir), "USDCAD ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: USDCAD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _usdCadExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("USDCHF"))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _usdChfSessionGate.AllowEntry(gateDir), "USDCHF SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: USDCHF SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _usdChfImpulseGate.AllowEntry(gateDir), "USDCHF ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: USDCHF ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _usdChfExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("BTCUSD"))
            {
                // BTC: direction mismatch safety
                TradeType routerTradeType =
                    _ctx.FinalDirection == TradeDirection.Long ? TradeType.Buy : TradeType.Sell;

                if (routerTradeType != gateDir)
                {
                    GlobalLogger.Log(_bot, $"[TC] ENTRY BLOCKED: Direction mismatch router={routerTradeType} gate={gateDir}");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _btcUsdSessionGate?.AllowEntry(gateDir) ?? false, "BTC SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: BTC SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _btcUsdImpulseGate?.AllowEntry(gateDir) ?? false, "BTC ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: BTC ImpulseGate");
                    return;
                }

                GlobalLogger.Log(_bot, "[BTC GATE] ALLOWED (Session+Impulse)");
                LogEntryExecuted(selected);
                _btcUsdExecutor?.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("ETHUSD"))
            {
                // ETH: direction mismatch safety
                TradeType routerTradeType =
                    _ctx.FinalDirection == TradeDirection.Long ? TradeType.Buy : TradeType.Sell;

                if (routerTradeType != gateDir)
                {
                    GlobalLogger.Log(_bot, $"[TC] ENTRY BLOCKED: Direction mismatch router={routerTradeType} gate={gateDir}");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _ethUsdSessionGate?.AllowEntry(gateDir) ?? false, "ETH SessionGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: ETH SessionGate");
                    return;
                }

                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _ethUsdImpulseGate?.AllowEntry(gateDir) ?? false, "ETH ImpulseGate"))
                {
                    GlobalLogger.Log(_bot, "[TC] BLOCKED: ETH ImpulseGate");
                    return;
                }

                GlobalLogger.Log(_bot, "[ETH GATE] ALLOWED (Session+Impulse)");
                LogEntryExecuted(selected);
                _ethUsdExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsGer40(_bot.SymbolName))
            {
                if (!EvaluateEntryGate(_ctx, selected, "SessionGate", () => _ger40SessionGate.AllowEntry(gateDir), "GER40 SessionGate")) return;
                if (!EvaluateEntryGate(_ctx, selected, "ImpulseGate", () => _ger40ImpulseGate.AllowEntry(gateDir), "GER40 ImpulseGate")) return;
                LogEntryExecuted(selected);
                _ger40Executor.ExecuteEntry(selected, _ctx);
            }
        }

        private bool ValidateDirectionConsistency(EntryContext entryContext, EntryEvaluation entry)
        {
            if (entryContext == null || entry == null)
            {
                GlobalLogger.Log(_bot, $"[DIR][FATAL_MISMATCH] type=null_context_or_entry sym={_bot.SymbolName}");
                GlobalLogger.Log(_bot, "[TC] ENTRY BLOCKED: direction consistency check failed");
                return false;
            }

            if (entryContext.FinalDirection == TradeDirection.None)
            {
                GlobalLogger.Log(_bot,
                    $"[DIR][FATAL_MISMATCH] type=final_none entry={entry.Direction} routed={entryContext.RoutedDirection} final={entryContext.FinalDirection} sym={_bot.SymbolName}");
                GlobalLogger.Log(_bot, "[TC] ENTRY BLOCKED: direction consistency check failed");
                return false;
            }

            if (entryContext.RoutedDirection != TradeDirection.None &&
                entryContext.RoutedDirection != entryContext.FinalDirection)
            {
                GlobalLogger.Log(_bot,
                    $"[DIR][FATAL_MISMATCH] type=routed_vs_final entry={entry.Direction} routed={entryContext.RoutedDirection} final={entryContext.FinalDirection} sym={_bot.SymbolName}");
                GlobalLogger.Log(_bot, "[TC] ENTRY BLOCKED: direction consistency check failed");
                return false;
            }

            if (entry.Direction != entryContext.FinalDirection)
            {
                GlobalLogger.Log(_bot,
                    $"[DIR][ENTRY_MISMATCH] entry={entry.Direction} routed={entryContext.RoutedDirection} final={entryContext.FinalDirection} sym={_bot.SymbolName}");
            }

            return true;
        }

        private static bool HasDirectionTraceCompleteness(EntryContext ctx)
        {
            return ctx != null
                && ctx.RoutedDirection != TradeDirection.None
                && ctx.FinalDirection != TradeDirection.None;
        }

        private static bool IsFxCandidate(EntryEvaluation candidate)
        {
            return candidate != null &&
                   candidate.Type.ToString().StartsWith("FX_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsIndexContinuationCandidate(EntryEvaluation candidate)
        {
            return candidate != null &&
                   candidate.Type.ToString().StartsWith("Index_", StringComparison.OrdinalIgnoreCase) &&
                   IsStrictContinuationType(candidate.Type);
        }

        private void LogIndexFunnelStage(EntryContext ctx, IReadOnlyCollection<EntryEvaluation> candidates, string stage)
        {
            if (_instrumentClass != InstrumentClass.INDEX || candidates == null)
                return;

            int count = candidates.Count(x => IsIndexContinuationCandidate(x) && x.IsValid);
            GlobalLogger.Log(_bot, $"[INDEX][FUNNEL] stage={stage} count={count}");
        }

        private static void LogFxEntryPipelineStage(EntryContext ctx, EntryEvaluation candidate, string stage)
        {
            if (ctx == null || !IsFxCandidate(candidate))
                return;

            GlobalLogger.Log(
                $"[FX][ENTRY_PIPELINE] symbol={candidate.Symbol ?? ctx.Symbol} entryType={candidate.Type} stage={stage} score={candidate.Score} confidence={candidate.LogicConfidence:0.##} isValid={candidate.IsValid.ToString().ToLowerInvariant()}");
        }

        private static void LogFxRejectReason(EntryContext ctx, EntryEvaluation candidate, string stage, string reason)
        {
            if (ctx == null || !IsFxCandidate(candidate) || candidate.IsValid)
                return;

            GlobalLogger.Log(
                $"[FX][REJECT_REASON] symbol={candidate.Symbol ?? ctx.Symbol} entryType={candidate.Type} stage={stage} reason={reason ?? candidate.Reason ?? "UNKNOWN"} score={candidate.Score} confidence={candidate.LogicConfidence:0.##}");
        }

        private static void LogFxRejectTransition(EntryContext ctx, EntryEvaluation candidate, string stage, bool wasValidBeforeStage)
        {
            if (!wasValidBeforeStage || candidate == null || candidate.IsValid)
                return;

            LogFxRejectReason(ctx, candidate, stage, candidate.Reason);
        }

        private EntryTraceSummary GetEntryTraceSummary(string symbol)
        {
            string key = string.IsNullOrWhiteSpace(symbol) ? _bot.SymbolName : symbol;
            if (!_entryTraceSummaries.TryGetValue(key, out var summary))
            {
                summary = new EntryTraceSummary();
                _entryTraceSummaries[key] = summary;
            }

            return summary;
        }

        private static void EnsureHtfClassification(EntryContext ctx, EntryEvaluation candidate)
        {
            if (candidate == null || !string.IsNullOrWhiteSpace(candidate.HtfClassification))
                return;

            TradeDirection htfAllowedDirection = ctx?.ActiveHtfDirection ?? TradeDirection.None;
            HtfClassificationModel.InitializeEntryHtfClassification(
                candidate,
                candidate.Direction,
                htfAllowedDirection);
        }

        private static TradeDirection GetEntryTraceCandidateDirection(EntryEvaluation candidate)
        {
            if (candidate == null)
                return TradeDirection.None;

            return candidate.HtfClassificationCandidateDirection != TradeDirection.None
                ? candidate.HtfClassificationCandidateDirection
                : candidate.Direction;
        }

        private void LogEntryTraceGate(
            EntryContext ctx,
            EntryEvaluation candidate,
            string gateName,
            TradeDirection beforeDirection,
            bool blocked,
            string reason)
        {
            if (candidate == null)
                return;

            EnsureHtfClassification(ctx, candidate);
            TradeDirection afterDirection = candidate.Direction;
            TradeDirection traceCandidateDirection = GetEntryTraceCandidateDirection(candidate);
            candidate.DirectionAfterGates = afterDirection;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ENTRY_TRACE][GATE] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} stage=GATE candidateDirection={traceCandidateDirection} score={candidate.Score} classification={candidate.HtfClassification ?? "HTF_NO_DIRECTION"} " +
                $"gateName={gateName} beforeDirection={beforeDirection} afterDirection={afterDirection} blocked={blocked.ToString().ToLowerInvariant()} reason={reason ?? "NA"}",
                ctx));

            if (beforeDirection != TradeDirection.None && afterDirection == TradeDirection.None)
            {
                candidate.LastDirectionDropStage = "GATE";
                candidate.LastDirectionDropModule = gateName;
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    $"[ENTRY_TRACE][DIRECTION_LOST] gateName={gateName} before={beforeDirection} after={afterDirection}",
                    ctx));
            }
        }

        private bool EvaluateEntryGate(EntryContext ctx, EntryEvaluation candidate, string gateName, Func<bool> evaluator, string blockedReason)
        {
            if (candidate == null)
            {
                GlobalLogger.Log(_bot, $"[ENTRY_TRACE][GATE] blocked=true reason=null_candidate symbol={ctx?.Symbol ?? _bot.SymbolName}");
                return false;
            }

            TradeDirection beforeDirection = candidate.Direction;
            bool allowed = evaluator != null && evaluator();
            bool blocked = !allowed;
            LogEntryTraceGate(ctx, candidate, gateName, beforeDirection, blocked, blocked ? blockedReason : "PASS");
            if (blocked)
            {
                candidate.LastDirectionDropStage = "GATE";
                candidate.LastDirectionDropModule = gateName;
            }

            return allowed;
        }

        private void LogEntryTraceSummary(EntryContext ctx, List<EntryEvaluation> symbolSignals)
        {
            if (ctx == null || symbolSignals == null)
                return;

            var summary = GetEntryTraceSummary(ctx.Symbol);
            summary.TotalEvaluations += symbolSignals.Count(e => e != null);
            summary.LogicProducedDirectionCount += symbolSignals.Count(e => e != null && e.RawDirection != TradeDirection.None);
            summary.NeverHadDirectionCount += symbolSignals.Count(e => e != null && e.RawDirection == TradeDirection.None);
            summary.LostAfterScoreCount += symbolSignals.Count(e => e != null && e.RawDirection != TradeDirection.None && e.DirectionAfterScore == TradeDirection.None);
            summary.LostAfterGateCount += symbolSignals.Count(e => e != null && e.DirectionAfterScore != TradeDirection.None && e.DirectionAfterGates == TradeDirection.None);

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ENTRY_TRACE][SUMMARY] symbol={ctx.Symbol} totalEvaluations={summary.TotalEvaluations} logicProducedDirectionCount={summary.LogicProducedDirectionCount} " +
                $"lostAfterScoreCount={summary.LostAfterScoreCount} lostAfterGateCount={summary.LostAfterGateCount} neverHadDirectionCount={summary.NeverHadDirectionCount}",
                ctx));
        }

        private void LogEntryTraceClassification(EntryContext ctx, EntryEvaluation candidate)
        {
            if (ctx == null || candidate == null)
                return;

            string classification;
            if (candidate.RawDirection == TradeDirection.None)
            {
                classification = "ENTRY_NO_SIGNAL";
            }
            else if (candidate.DirectionAfterScore == TradeDirection.None || candidate.Score < EntryDecisionPolicy.MinScoreThreshold)
            {
                classification = "ENTRY_SCORE_FAIL";
            }
            else if (candidate.DirectionAfterGates == TradeDirection.None || !candidate.IsValid)
            {
                classification = "ENTRY_GATE_BLOCK";
            }
            else
            {
                classification = "ENTRY_UNKNOWN";
            }

            candidate.EntryTraceClassification = classification;
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ENTRY_TRACE][CLASSIFICATION] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} stage=CLASSIFICATION candidateDirection={candidate.Direction} score={candidate.Score} classification={classification}",
                ctx));
        }

        private void LogCriticalDirectionDrop(EntryContext ctx, EntryEvaluation candidate)
        {
            if (ctx == null || candidate == null)
                return;

            TradeDirection finalCandidateDirection = candidate.DirectionAfterGates;

            if (candidate.RawDirection != TradeDirection.None && finalCandidateDirection == TradeDirection.None)
            {
                string lostAtStage = candidate.LastDirectionDropStage ?? "FINAL";
                string dropModule = candidate.LastDirectionDropModule ?? "UNKNOWN";
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    $"[ENTRY_TRACE][CRITICAL_DIRECTION_DROP] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} " +
                    $"lostAtStage={lostAtStage} lastValidDirection={candidate.RawDirection} dropModule={dropModule}",
                    ctx));
            }
        }

        private void ApplyTransitionScoreBoost(EntryContext ctx, List<EntryEvaluation> symbolSignals)
        {
            if (ctx == null || symbolSignals == null)
                return;

            int transitionBonus = ctx.TransitionValid ? Math.Max(0, ctx.TransitionScoreBonus) : 0;
            int flagBreakoutBonus = ctx.FlagBreakoutConfirmed ? 10 : 0;
            if (transitionBonus <= 0 && flagBreakoutBonus <= 0)
                return;

            foreach (var entry in symbolSignals)
            {
                if (entry == null || entry.Direction == TradeDirection.None || EntryDecisionPolicy.IsHardInvalid(entry))
                    continue;

                int boost = 0;
                if (transitionBonus > 0)
                    boost += GetTransitionBoost(entry.Type, transitionBonus);

                if (flagBreakoutBonus > 0)
                    boost += GetFlagBreakoutBoost(entry.Type, flagBreakoutBonus);

                if (boost <= 0)
                    continue;

                entry.Score += boost;
                if (entry.Score > 100)
                    entry.Score = 100;

                entry.Reason = $"{entry.Reason} [STRUCTURE+{boost}]";
                GlobalLogger.Log(_bot, $"[ENTRY][STRUCTURE] score boost applied type={entry.Type} boost={boost} score={entry.Score} transition={ctx.TransitionValid} breakout={ctx.FlagBreakoutConfirmed}");
            }
        }

        private static int GetFlagBreakoutBoost(EntryType type, int maxBonus)
        {
            switch (type)
            {
                case EntryType.FX_Flag:
                case EntryType.FX_FlagContinuation:
                case EntryType.Index_Flag:
                case EntryType.Crypto_Flag:
                case EntryType.TC_Flag:
                case EntryType.XAU_Flag:
                    return maxBonus;

                case EntryType.FX_Pullback:
                case EntryType.Index_Pullback:
                case EntryType.Crypto_Pullback:
                case EntryType.TC_Pullback:
                case EntryType.XAU_Pullback:
                    return Math.Min(maxBonus, 8);

                default:
                    return 0;
            }
        }

        private static int GetTransitionBoost(EntryType type, int maxBonus)
        {
            switch (type)
            {
                case EntryType.FX_Flag:
                case EntryType.FX_FlagContinuation:
                case EntryType.Index_Flag:
                case EntryType.Crypto_Flag:
                case EntryType.TC_Flag:
                case EntryType.XAU_Flag:
                    return maxBonus;

                case EntryType.FX_Pullback:
                case EntryType.Index_Pullback:
                case EntryType.Crypto_Pullback:
                case EntryType.TC_Pullback:
                case EntryType.XAU_Pullback:
                    return Math.Min(maxBonus, 8);

                default:
                    return 0;
            }
        }

        private void ApplyRestartProtection(EntryContext ctx, List<EntryEvaluation> symbolSignals)
        {
            if (ctx == null || symbolSignals == null || symbolSignals.Count == 0)
                return;

            foreach (var candidate in symbolSignals)
            {
                if (candidate == null || candidate.Direction == TradeDirection.None)
                    continue;

                TradeDirection beforeDirection = candidate.Direction;

                if (!TryGetRestartDecayState(ctx, candidate, out string restartReason))
                {
                    LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, false, "PASS_NO_RESTART_DECAY");
                    continue;
                }

                if (!candidate.IsValid)
                {
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[INTEGRITY] SKIP restart protect on invalid candidate: {candidate.Type}",
                        ctx));
                    LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, true, "SKIP_INVALID_CANDIDATE");
                    continue;
                }

                if (BotRestartState.IsHardProtectionPhase)
                {
                    bool isCryptoCandidate = IsCryptoCandidate(candidate.Type);
                    bool continuationAuthority = HasContinuationAuthority(ctx, candidate);
                    bool midTrend =
                        ctx.TrendDirection == candidate.Direction &&
                        ctx.MarketState?.IsTrend == true;
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY][AUTH] source=RESTART_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} dir={candidate.Direction} authority={continuationAuthority}",
                        ctx));
                    bool freshDirectionalContinuation =
                        isCryptoCandidate &&
                        ctx.BarsSinceStart <= 1 &&
                        candidate.Direction != TradeDirection.None &&
                        restartReason != "StaleImpulse" &&
                        (ctx.GetBarsSinceImpulse(candidate.Direction) <= 2 || ctx.HasDirectionalPullback(candidate.Direction));

                    if (!freshDirectionalContinuation)
                    {
                        if (continuationAuthority)
                        {
                            int protectedPenalty = 14;
                            int protectedOriginalScore = candidate.Score;
                            candidate.Score = Math.Max(EntryDecisionPolicy.MinScoreThreshold, candidate.Score - protectedPenalty);
                            candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                                ? "[RESTART_PROTECT_SUPPRESSED_CONTINUATION_AUTH]"
                                : $"{candidate.Reason} [RESTART_PROTECT_SUPPRESSED_CONTINUATION_AUTH]";
                            EntryDecisionPolicy.Normalize(candidate);

                            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                                $"[ENTRY][PROTECT_SUPPRESSED] source=RESTART_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                                $"type={candidate.Type} dir={candidate.Direction} score={protectedOriginalScore}->{candidate.Score} state={restartReason}",
                                ctx));
                            LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, false, $"SOFT_CONTINUATION_AUTH_{restartReason}");
                            continue;
                        }
                        else if (midTrend)
                        {
                            int restartPenalty = 14;
                            restartPenalty = Math.Min(restartPenalty, 8);
                            int midTrendOriginalScore = candidate.Score;
                            candidate.Score = Math.Max(EntryDecisionPolicy.MinScoreThreshold, candidate.Score - restartPenalty);
                            candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                                ? $"[RESTART_MID_TREND_SOFT_{restartReason}]"
                                : $"{candidate.Reason} [RESTART_MID_TREND_SOFT_{restartReason}]";
                            EntryDecisionPolicy.Normalize(candidate);

                            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                                $"[ENTRY][MID_TREND] active=true restartPenalty={restartPenalty} earlyBreakPenalty=0 symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} dir={candidate.Direction}",
                                ctx));
                            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                                $"[ENTRY][PROTECT] source=RESTART_PROTECT action=MID_TREND_SOFT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                                $"type={candidate.Type} dir={candidate.Direction} score={midTrendOriginalScore}->{candidate.Score} state={restartReason}",
                                ctx));
                            LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, false, $"SOFT_MID_TREND_{restartReason}");
                            continue;
                        }

                        candidate.IsValid = false;
                        candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                            ? "[RESTART_DECAY_AFTER_RESTART]"
                            : $"{candidate.Reason} [RESTART_DECAY_AFTER_RESTART]";
                        EntryDecisionPolicy.Normalize(candidate);

                        GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                            $"[ENTRY][PROTECT] source=RESTART_PROTECT action=HARD_BLOCK symbol={candidate.Symbol ?? _bot.SymbolName} " +
                            $"type={candidate.Type} dir={candidate.Direction} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                            ctx));
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                            $"[RESTART BLOCK] reason=DECAY_AFTER_RESTART symbol={candidate.Symbol ?? _bot.SymbolName} " +
                            $"type={candidate.Type} dir={candidate.Direction} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                            ctx));
                        LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, true, "HARD_BLOCK_DECAY_AFTER_RESTART");
                        continue;
                    }

                    int hardPhaseCryptoPenalty = 12;
                    int originalScore = candidate.Score;
                    candidate.Score = Math.Max(CryptoSurvivableScoreFloor, candidate.Score - hardPhaseCryptoPenalty);
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? $"[RESTART_SOFT_AFTER_RESTART_{restartReason}]"
                        : $"{candidate.Reason} [RESTART_SOFT_AFTER_RESTART_{restartReason}]";
                    EntryDecisionPolicy.Normalize(candidate);

                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY][PROTECT] source=RESTART_PROTECT action=SOFT_PENALTY symbol={candidate.Symbol ?? _bot.SymbolName} " +
                        $"type={candidate.Type} dir={candidate.Direction} score={originalScore}->{candidate.Score} state={restartReason}",
                        ctx));
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[RESTART SOFT-HARDPHASE] symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} " +
                        $"dir={candidate.Direction} score={originalScore}->{candidate.Score} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                        ctx));
                    LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, false, $"SOFT_PENALTY_{restartReason}");
                    continue;
                }

                if (!BotRestartState.IsSoftProtectionPhase)
                {
                    LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, false, "PASS_NO_SOFT_PHASE");
                    continue;
                }

                const int softPenalty = 20;
                int originalSoftScore = candidate.Score;
                candidate.Score = Math.Max(0, candidate.Score - softPenalty);
                candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? $"[RESTART_SOFT_{restartReason}]"
                    : $"{candidate.Reason} [RESTART_SOFT_{restartReason}]";
                EntryDecisionPolicy.Normalize(candidate);

                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    $"[RESTART SOFT] penalty applied symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} " +
                    $"dir={candidate.Direction} score={originalSoftScore}->{candidate.Score} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                    ctx));
                LogEntryTraceGate(ctx, candidate, nameof(ApplyRestartProtection), beforeDirection, false, $"SOFT_PHASE_PENALTY_{restartReason}");
            }
        }

        private static bool TryGetRestartDecayState(EntryContext ctx, EntryEvaluation candidate, out string restartReason)
        {
            restartReason = null;
            if (ctx == null || candidate == null)
                return false;

            string reason = candidate.Reason ?? string.Empty;
            int barsSinceImpulse = ctx.GetBarsSinceImpulse(candidate.Direction);
            bool hasDirectionalPullback = ctx.HasDirectionalPullback(candidate.Direction);

            bool staleImpulse =
                ContainsAny(reason, "STALE_IMPULSE", "IMPULSE_TOO_OLD") ||
                barsSinceImpulse > 6;

            bool pullbackNotFormed =
                ContainsAny(reason, "NO_PULLBACK", "PULLBACK_NOT_MATURE", "PULLBACK_TOO_EARLY", "NO_DECELERATION") ||
                !hasDirectionalPullback;

            bool impulseDecay =
                ContainsAny(reason, "DECAY", "IMPULSE_COOLDOWN") ||
                (barsSinceImpulse <= 2 && !ctx.IsPullbackDecelerating_M5) ||
                (barsSinceImpulse <= 2 && !ctx.HasReactionCandle_M5 && !ctx.HasDirectionalPullback(candidate.Direction));

            if (impulseDecay)
                restartReason = "ImpulseDecay";
            else if (staleImpulse)
                restartReason = "StaleImpulse";
            else if (pullbackNotFormed)
                restartReason = "PullbackNotFormed";

            return restartReason != null;
        }

        private void LogEntrySnapshot(EntryContext ctx, EntryEvaluation selected)
        {
            if (ctx == null || selected == null)
                return;

            if (ctx.FinalDirection == TradeDirection.None ||
                ctx.EntryScore <= 0 ||
                ctx.FinalConfidence <= 0 ||
                ctx.RiskConfidence <= 0)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    "[SNAPSHOT][SKIP][MISSING_FINAL_STATE]",
                    ctx));
                return;
            }

            GlobalLogger.Log(_bot, $"[SNAPSHOT][FINAL] dir={ctx.FinalDirection} FC={ctx.FinalConfidence} RC={ctx.RiskConfidence}");

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                TradeAuditLog.BuildEntrySnapshot(_bot, ctx, selected),
                ctx));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                TradeAuditLog.BuildDirectionSnapshot(ctx, selected),
                ctx));
        }

        private string ResolveEntrySnapshotRegime(EntryContext ctx)
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

            return _instrumentClass.ToString();
        }

        private void SyncMemoryState(EntryContext ctx)
        {
            if (ctx == null || string.IsNullOrWhiteSpace(ctx.Symbol))
                return;

            string memorySymbol = NormalizeSymbol(ctx.Symbol);

            if (ctx.M5 == null || ctx.M5.Count <= 0)
            {
                ctx.MemoryState = _memoryEngine.GetState(memorySymbol);
                ctx.MemoryResolved = ctx.MemoryState?.IsResolved == true;
                ctx.MemoryUsable = ctx.MemoryState?.IsUsable == true;
                ctx.MemoryAssessment = _memoryEngine.GetAssessment(memorySymbol);
                return;
            }

            SymbolMemoryState state = _memoryEngine.GetState(memorySymbol);
            _memoryEngine.MarkResolved(memorySymbol);

            if (state == null || !state.IsBuilt || state.BuildMode == MemoryBuildMode.Default)
            {
                state = RecoverMissingMemoryState(memorySymbol, state, "sync");
            }
            else
            {
                int lastClosedIndex = Math.Max(0, ctx.M5.Count - 2);
                _memoryEngine.OnBar(memorySymbol, ctx.M5[lastClosedIndex]);
            }

            state = _memoryEngine.GetState(memorySymbol);
            state.SessionName = ctx.Session.ToString();
            state.SessionFatigueScore = Math.Max(0, ctx.BarsSinceStart);

            ctx.MemoryState = state;
            ctx.MemoryResolved = state.IsResolved;
            ctx.MemoryUsable = state.IsUsable;
            ctx.MemoryAssessment = _memoryEngine.GetAssessment(memorySymbol);
        }

        private SymbolMemoryState RecoverMissingMemoryState(string symbol, SymbolMemoryState currentState, string source)
        {
            string normalized = NormalizeSymbol(symbol);
            bool resolverOk = _runtimeSymbols.TryResolveSymbol(normalized, out _);

            if (!resolverOk)
            {
                _memoryEngine.MarkResolveFailure(normalized, "unresolved_runtime_symbol");
                return currentState ?? _memoryEngine.GetState(normalized);
            }

            GlobalLogger.Log(_bot, $"[MEMORY][RECOVER] symbol={normalized} source={source}");
            _memoryEngine.BuildFromHistory(normalized, LoadMemoryHistory(normalized));

            SymbolMemoryState rebuiltState = _memoryEngine.GetState(normalized);
            if (rebuiltState == null || !rebuiltState.IsBuilt)
            {
                GlobalLogger.Log(_bot, $"[MEMORY][CRITICAL_MISSING] symbol={normalized} source={source}");
            }

            return rebuiltState;
        }

        private static List<Bar> ToClosedBarList(Bars bars)
        {
            var result = new List<Bar>();
            if (bars == null)
                return result;

            int closedCount = Math.Max(0, bars.Count - 1);
            for (int i = 0; i < closedCount; i++)
            {
                result.Add(bars[i]);
            }

            return result;
        }

        private void EnsureStartupMemoryReady()
        {
            if (_isMemoryReady)
                return;

            var symbols = GetTrackedCanonicalSymbols();

            foreach (var symbol in symbols)
            {
                if (DebugStartupTrace)
                    GlobalLogger.Log(_bot, $"[STARTUP][TRACE] before_resolve symbol={symbol}");

                var runtimeSymbol = ResolveSymbol(symbol);

                if (DebugStartupTrace)
                    GlobalLogger.Log(_bot, $"[STARTUP][TRACE] after_resolve symbol={symbol} resolved={(runtimeSymbol != null)}");

                if (!IsTradable(runtimeSymbol))
                {
                    GlobalLogger.Log(_bot, "BLOCK: not tradable");
                    GlobalLogger.Log(_bot, $"[MEMORY][SKIP] {symbol}");
                    continue;
                }

                if (DebugStartupTrace)
                    GlobalLogger.Log(_bot, $"[STARTUP][TRACE] initialize_memory symbol={symbol}");

                _memoryEngine.Initialize(symbol);
                _memoryEngine.BuildFromHistory(symbol, LoadMemoryHistory(symbol));
            }

            _isMemoryReady = true;
            EmitStartupCoverageLogs(symbols);
            GlobalLogger.Log(_bot, $"[BOOT][MEMORY_READY] symbols={symbols.Count}");
        }

        private List<Bar> LoadMemoryHistory(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return new List<Bar>();

            if (!_runtimeSymbols.TryGetBars(TimeFrame.Minute5, symbol, out Bars bars))
            {
                string normalizedSymbol = NormalizeSymbol(symbol);
                _memoryEngine.MarkResolveFailure(normalizedSymbol, "unresolved_runtime_symbol");
                GlobalLogger.Log(_bot, $"[MEMORY][SYMBOL_UNRESOLVED] canonical={normalizedSymbol}");
                return new List<Bar>();
            }

            _memoryEngine.MarkResolved(NormalizeSymbol(symbol));

            return ToClosedBarList(bars);
        }

        private static bool ContainsAny(string value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value) || tokens == null)
                return false;

            foreach (var token in tokens)
            {
                if (!string.IsNullOrWhiteSpace(token) &&
                    value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private bool HasContinuationAuthority(EntryContext ctx, EntryEvaluation candidate)
        {
            if (ctx == null || candidate == null || candidate.Direction == TradeDirection.None)
                return false;

            bool trendAligned = ctx.TrendDirection == candidate.Direction;
            bool impulseAligned = ctx.HasImpulse_M5;
            bool atrAligned = ctx.IsAtrExpanding_M5;
            bool trendStateOk = ctx.MarketState?.IsTrend == true;

            return trendAligned && impulseAligned && atrAligned && trendStateOk;
        }

        private void ApplyManagedEarlyBreakTriggers(EntryContext ctx, EntryEvaluation candidate, int barsSinceBreak)
        {
            if (ctx == null || candidate == null)
                return;

            int originalScore = candidate.Score;
            int earlyBreakPenalty = 15;
            bool continuationAuthority = HasContinuationAuthority(ctx, candidate);
            bool midTrend =
                ctx.TrendDirection == candidate.Direction &&
                ctx.MarketState?.IsTrend == true;
            bool hasRestartPenalty = ContainsAny(candidate.Reason, "RESTART_");
            bool hasEarlyBreakPenalty = earlyBreakPenalty > 0;

            if (!continuationAuthority && midTrend && hasRestartPenalty && hasEarlyBreakPenalty)
            {
                earlyBreakPenalty = (int)(earlyBreakPenalty * 0.5);
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    $"[ENTRY][MID_TREND] active=true restartPenalty=carried earlyBreakPenalty={earlyBreakPenalty} symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} dir={candidate.Direction}",
                    ctx));
            }

            int appliedPenalty = earlyBreakPenalty;

            if (continuationAuthority)
            {
                const int continuationPenaltyCap = 10;
                appliedPenalty = Math.Min(appliedPenalty, continuationPenaltyCap);
            }

            candidate.Score = Math.Max(0, candidate.Score - appliedPenalty);
            candidate.Reason = $"{candidate.Reason} [EARLY_BREAK_PENALTY]";
            if (candidate.Type == EntryType.Index_Flag && candidate.TriggerConfirmed)
            {
                GlobalLogger.Log(_bot, 
                    $"[AUDIT][EARLY BREAK] type={candidate.Type} " +
                    $"penalty={appliedPenalty} " +
                    $"scoreBefore={originalScore} " +
                    $"scoreAfter={candidate.Score}");
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ENTRY][AUTH] source=EARLY_BREAK_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                $"type={candidate.Type} dir={candidate.Direction} authority={continuationAuthority}",
                ctx));

            if (continuationAuthority)
            {
                GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                    $"[ENTRY][PROTECT_SUPPRESSED] source=EARLY_BREAK_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                    $"type={candidate.Type} dir={candidate.Direction} score={originalScore}->{candidate.Score} penalty={appliedPenalty} barsSinceBreak={barsSinceBreak}",
                    ctx));
                return;
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                $"[ENTRY][PROTECT] source=EARLY_BREAK_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                $"type={candidate.Type} dir={candidate.Direction} score={originalScore}->{candidate.Score} penalty={appliedPenalty} barsSinceBreak={barsSinceBreak}",
                ctx));
        }

        private void UpdateExecutionStateMachine(EntryContext ctx, List<EntryEvaluation> symbolSignals)
        {
            if (ctx == null || symbolSignals == null)
                return;

            foreach (var candidate in symbolSignals)
            {
                if (candidate == null)
                    continue;

                bool upstreamTriggerState = candidate.TriggerConfirmed;
                EntryState upstreamState = candidate.State;
                candidate.TriggerConfirmed = false;
                candidate.State = EntryState.NONE;

                if (candidate.Direction == TradeDirection.None || EntryDecisionPolicy.IsHardInvalid(candidate))
                {
                    ClearArmedSetup(candidate);
                    continue;
                }

                if (candidate.Score > 0)
                    candidate.State = EntryState.SETUP_DETECTED;

                int barsSinceBreak = GetBarsSinceBreak(ctx, candidate.Direction);
                bool lateContinuationForCandidate =
                    (candidate.Direction == TradeDirection.Long && ctx.HasLateContinuationLong) ||
                    (candidate.Direction == TradeDirection.Short && ctx.HasLateContinuationShort);
                GlobalLogger.Log(_bot, $"[TIMING][{candidate.Type}] barsSinceBreak={barsSinceBreak} late={lateContinuationForCandidate.ToString().ToLowerInvariant()}");
                if (barsSinceBreak == 0)
                    ApplyManagedEarlyBreakTriggers(ctx, candidate, barsSinceBreak);

                if (candidate.Score < EntryDecisionPolicy.MinScoreThreshold)
                {
                    ClearArmedSetup(candidate);
                    continue;
                }

                var trigger = ResolveTriggerDiagnostics(ctx, candidate);
                candidate.HasStrongTrigger = trigger.TriggerConfirmed;
                candidate.HasStrongStructure = HasStrongStructure(ctx, candidate.Direction);

                if (HasInvalidTriggerIntegrity(candidate, trigger, upstreamTriggerState, upstreamState, out string triggerInvalidReason))
                {
                    candidate.IsValid = false;
                    candidate.TriggerConfirmed = false;
                    candidate.State = EntryState.NONE;
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? "[TRIGGER_INVALID]"
                        : $"{candidate.Reason} [TRIGGER_INVALID]";
                    ClearArmedSetup(candidate);
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][BLOCK][TRIGGER_INVALID]\nreason={triggerInvalidReason}\nentryType={candidate.Type}\ninstrument={candidate.Symbol ?? ctx?.Symbol ?? _bot.SymbolName}");
                    continue;
                }

                if (ShouldRejectEarlyNoStructure(ctx, candidate, candidate.HasStrongTrigger))
                {
                    MovePhase movePhase;
                    bool phaseFromCtx = ctx.MovePhase != MovePhase.Unknown;
                    if (phaseFromCtx)
                    {
                        movePhase = ctx.MovePhase;
                    }
                    else if (ctx.MemoryState != null)
                    {
                        movePhase = ctx.MemoryState.MovePhase;
                    }
                    else
                    {
                        movePhase = MovePhase.Unknown;
                    }
                    candidate.IsValid = false;
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? "[EARLY_NO_STRUCTURE]"
                        : $"{candidate.Reason} [EARLY_NO_STRUCTURE]";
                    ClearArmedSetup(candidate);
                    GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                        $"[ENTRY][REJECT][EARLY_NO_STRUCTURE] {candidate.Symbol ?? _bot.SymbolName} {candidate.Type} {candidate.Direction} phase={movePhase} source={(phaseFromCtx ? "CTX" : "MEM")} barsSinceBreak={barsSinceBreak} pullback={ctx.BarsSinceFirstPullback} score={candidate.Score}",
                        ctx));
                    continue;
                }

                GlobalLogger.Log(_bot, $"[TRIGGER] type={candidate.Type} confirmed={trigger.TriggerConfirmed}");
                candidate.TriggerConfirmed = trigger.TriggerConfirmed;
                GlobalLogger.Log(_bot, $"[TRIGGER STATE] candidate={candidate.TriggerConfirmed}");

                int currentBarIndex = ctx.M5?.Count - 1 ?? -1;
                int triggerBarIndex = barsSinceBreak >= 0 ? currentBarIndex - barsSinceBreak : currentBarIndex;
                int triggerAgeBars = currentBarIndex - triggerBarIndex;

                if (candidate.TriggerConfirmed && triggerAgeBars > 3)
                {
                    GlobalLogger.Log(_bot, "[TRIGGER BLOCK] Stale trigger");
                    candidate.TriggerConfirmed = false;
                }

                bool transitionFilterApplied = false;

                if (!ApplyContinuationTransitionNoMomentumFilter(ctx, candidate, out transitionFilterApplied))
                {
                    ClearArmedSetup(candidate);
                    continue;
                }

                if (transitionFilterApplied)
                {
                    string instrument = candidate.Symbol ?? ctx.Symbol ?? _bot.SymbolName;
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][FILTER][SKIPPED] instrument={instrument} entryType={candidate.Type} reason=already_filtered");
                }
                else if (!ApplyContinuationWeakStructureFilter(ctx, candidate))
                {
                    ClearArmedSetup(candidate);
                    continue;
                }

                if (!trigger.IsManaged)
                {
                    if (candidate.TriggerConfirmed)
                    {
                        candidate.State = EntryState.TRIGGERED;
                    }
                    else
                    {
                        candidate.State = EntryState.ARMED;
                    }

                    if (!candidate.TriggerConfirmed && candidate.State == EntryState.TRIGGERED)
                    {
                        GlobalLogger.Log(_bot, $"[INTEGRITY ERROR] Illegal state: TRIGGERED without trigger | type={candidate.Type}");
                    }

                    GlobalLogger.Log(_bot, $"[TRIGGER FINAL] type={candidate.Type} trigger={candidate.TriggerConfirmed} state={candidate.State}");
                    continue;
                }

                if (!trigger.TriggerConfirmed)
                {
                    UpsertArmedSetup(candidate, barsSinceBreak, false, currentBarIndex);
                    GlobalLogger.Log(_bot, $"[SETUP DETECTED] symbol={candidate.Symbol} score={candidate.Score} state=ARMED type={candidate.Type} dir={candidate.Direction}");
                    GlobalLogger.Log(_bot, $"[TRIGGER WAIT] symbol={candidate.Symbol} reason={trigger.WaitReason} type={candidate.Type} dir={candidate.Direction} impact=score_only");
                }
                else
                {
                    UpsertArmedSetup(candidate, barsSinceBreak, true, currentBarIndex);
                    GlobalLogger.Log(_bot, $"[TRIGGER CONFIRMED] symbol={candidate.Symbol} breakoutClose={trigger.BreakoutClose.ToString().ToLowerInvariant()} structureBreak={trigger.StructureBreak.ToString().ToLowerInvariant()} m1Break={trigger.M1Break.ToString().ToLowerInvariant()} type={candidate.Type} dir={candidate.Direction}");
                }

                if (candidate.TriggerConfirmed && _armedSetups.TryGetValue(GetArmedSetupKey(candidate), out var armed))
                {
                    int barsSinceTrigger = armed.TriggerBarIndex >= 0
                        ? Math.Max(0, currentBarIndex - armed.TriggerBarIndex)
                        : 0;

                    if (barsSinceTrigger > 1)
                    {
                        int scoreBefore = candidate.Score;
                        candidate.Score = Math.Max(0, (int)Math.Round(candidate.Score * 0.75, MidpointRounding.AwayFromZero));
                        GlobalLogger.Log(_bot, TradeLogIdentity.WithTempId(
                            $"[TRIGGER][LATE_PENALTY] symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} dir={candidate.Direction} barsSinceTrigger={barsSinceTrigger} score={scoreBefore}->{candidate.Score}",
                            ctx));
                    }
                }

                if (candidate.TriggerConfirmed)
                {
                    candidate.State = EntryState.TRIGGERED;
                }
                else
                {
                    candidate.State = EntryState.ARMED;
                }

                if (!candidate.TriggerConfirmed && candidate.State == EntryState.TRIGGERED)
                {
                    GlobalLogger.Log(_bot, $"[INTEGRITY ERROR] Illegal state: TRIGGERED without trigger | type={candidate.Type}");
                }

                GlobalLogger.Log(_bot, $"[TRIGGER FINAL] type={candidate.Type} trigger={candidate.TriggerConfirmed} state={candidate.State}");
            }
        }

        private TriggerDiagnostics ResolveTriggerDiagnostics(EntryContext ctx, EntryEvaluation candidate)
        {
            var diagnostics = new TriggerDiagnostics();
            if (ctx == null || candidate == null)
                return diagnostics;

            diagnostics.IsManaged = IsTriggerManaged(candidate.Type);
            if (!diagnostics.IsManaged)
            {
                diagnostics.TriggerConfirmed = true;
                return diagnostics;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Reason) &&
                candidate.Reason.IndexOf("WAIT_BREAKOUT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                diagnostics.WaitReason = "WAIT_BREAKOUT";
                diagnostics.TriggerConfirmed = false;
                return diagnostics;
            }

            int lastClosedM5 = ctx.M5?.Count >= 2 ? ctx.M5.Count - 2 : -1;
            double buffer = ctx.FlagAtr_M5 > 0 ? ctx.FlagAtr_M5 * 0.10 : ctx.AtrM5 * 0.10;

            if (lastClosedM5 >= 1)
            {
                double rangeHigh = ctx.FlagHigh;
                double rangeLow = ctx.FlagLow;

                if (rangeHigh > rangeLow)
                {
                    double close = ctx.M5.ClosePrices[lastClosedM5];
                    if (candidate.Direction == TradeDirection.Long)
                    {
                        diagnostics.BreakoutClose = close > rangeHigh + buffer;
                    }
                    else if (candidate.Direction == TradeDirection.Short)
                    {
                        diagnostics.BreakoutClose = close < rangeLow - buffer;
                    }
                }

                double prevHigh = ctx.M5.HighPrices[lastClosedM5 - 1];
                double prevLow = ctx.M5.LowPrices[lastClosedM5 - 1];
                double currentHigh = ctx.M5.HighPrices[lastClosedM5];
                double currentLow = ctx.M5.LowPrices[lastClosedM5];

                if (candidate.Direction == TradeDirection.Long)
                {
                    diagnostics.StructureBreak =
                        currentHigh > prevHigh ||
                        ctx.BrokeLastSwingHigh_M5;
                }
                else if (candidate.Direction == TradeDirection.Short)
                {
                    diagnostics.StructureBreak =
                        currentLow < prevLow ||
                        ctx.BrokeLastSwingLow_M5;
                }
            }

            diagnostics.M1Break =
                ctx.HasBreakout_M1 &&
                ctx.BreakoutDirection == candidate.Direction;

            if (candidate.Direction == TradeDirection.None)
            {
                diagnostics.WaitReason = "INVALID_TRIGGER_DIRECTION";
                diagnostics.TriggerConfirmed = false;
                return diagnostics;
            }

            diagnostics.TriggerConfirmed =
                diagnostics.BreakoutClose ||
                diagnostics.StructureBreak ||
                diagnostics.M1Break;

            if (!diagnostics.TriggerConfirmed && string.IsNullOrWhiteSpace(diagnostics.WaitReason))
                diagnostics.WaitReason = "WAIT_BREAKOUT";

            return diagnostics;
        }

        private static bool IsTriggerManaged(EntryType type)
        {
            switch (type)
            {
                case EntryType.FX_Flag:
                case EntryType.FX_MicroStructure:
                case EntryType.FX_RangeBreakout:
                case EntryType.FX_FlagContinuation:
                case EntryType.FX_MicroContinuation:
                case EntryType.FX_ImpulseContinuation:
                case EntryType.Index_Flag:
                case EntryType.Index_Breakout:
                case EntryType.Crypto_Flag:
                case EntryType.Crypto_RangeBreakout:
                case EntryType.XAU_Flag:
                case EntryType.XAU_Impulse:
                case EntryType.TC_Flag:
                    return true;

                default:
                    return false;
            }
        }

        private static bool HasInvalidTriggerIntegrity(
            EntryEvaluation candidate,
            TriggerDiagnostics trigger,
            bool upstreamTriggerState,
            EntryState upstreamState,
            out string reason)
        {
            reason = null;
            if (candidate == null || trigger == null)
            {
                reason = "missing_candidate_or_trigger";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(candidate.Reason) &&
                (candidate.Reason.IndexOf("forced", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 candidate.Reason.IndexOf("enforced", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                reason = "forced_trigger_marker_detected";
                return true;
            }

            if (upstreamState == EntryState.TRIGGERED && !trigger.TriggerConfirmed)
            {
                reason = "state_triggered_without_valid_trigger";
                return true;
            }

            if (upstreamTriggerState && !trigger.TriggerConfirmed)
            {
                reason = "upstream_trigger_true_but_runtime_trigger_false";
                return true;
            }

            if (candidate.Direction == TradeDirection.None)
            {
                reason = "invalid_direction_for_trigger";
                return true;
            }

            return false;
        }

        private bool ApplyContinuationTransitionNoMomentumFilter(EntryContext ctx, EntryEvaluation candidate, out bool filterApplied)
        {
            filterApplied = false;

            if (ctx == null || candidate == null)
                return true;

            if (!candidate.IsValid)
            {
                filterApplied = true;
                GlobalLogger.Log(_bot,
                    $"[FILTER][SKIPPED_ALREADY_INVALID] symbol={candidate.Symbol ?? ctx.Symbol ?? _bot.SymbolName} entryType={candidate.Type} filter=TRANSITION_NO_MOMENTUM");
                return true;
            }

            if (!IsStrictContinuationType(candidate.Type))
                return true;

            bool isTransition = ctx.IsTransition_M5;
            bool? hasMomentum = ctx.MarketState?.IsMomentum;
            string instrument = candidate.Symbol ?? ctx.Symbol ?? _bot.SymbolName;
            TransitionEvaluation transition = candidate.Direction == TradeDirection.Long
                ? ctx.TransitionLong
                : candidate.Direction == TradeDirection.Short
                    ? ctx.TransitionShort
                    : ctx.Transition;
            double rawTransitionQuality = transition?.QualityScore ?? ctx.Transition?.QualityScore ?? 0.0;
            double transitionQuality = transition?.QualityScore01 ?? ctx.Transition?.QualityScore01 ?? 0.0;

            if (!hasMomentum.HasValue)
            {
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][TRANSITION_NO_MOMENTUM] rawTQ={rawTransitionQuality:0.00} tq={transitionQuality:0.00} action=none instrument={instrument} entryType={candidate.Type} momentum=missing transition={isTransition.ToString().ToLowerInvariant()}");
                return true;
            }

            if (!isTransition || hasMomentum.Value)
            {
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][TRANSITION_NO_MOMENTUM] rawTQ={rawTransitionQuality:0.00} tq={transitionQuality:0.00} action=none instrument={instrument} entryType={candidate.Type} momentum={hasMomentum.Value.ToString().ToLowerInvariant()} transition={isTransition.ToString().ToLowerInvariant()}");
                return true;
            }

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(instrument);
            int penalty = 0;
            bool block = false;

            if (instrumentClass == InstrumentClass.INDEX && IsContinuationMomentumType(candidate.Type))
            {
                filterApplied = true;
                bool hasTrend = ctx.QualificationState?.HasTrend == true;
                bool isMomentum = hasMomentum.Value;
                bool isLowVol = ctx.MarketState?.IsLowVol == true;
                bool shouldHardBlock =
                    !hasTrend &&
                    !isMomentum &&
                    isLowVol &&
                    transitionQuality < 0.40;

                GlobalLogger.Log(_bot,
                    $"[INDEX][NO_MOMENTUM] tq={transitionQuality:0.00} hasTrend={hasTrend.ToString().ToLowerInvariant()} isMomentum={isMomentum.ToString().ToLowerInvariant()} isLowVol={isLowVol.ToString().ToLowerInvariant()} action={(shouldHardBlock ? "block" : "penalty")}");

                if (shouldHardBlock)
                {
                    candidate.IsValid = false;
                    candidate.TriggerConfirmed = false;
                    candidate.State = EntryState.NONE;
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? "[NO_MOMENTUM_INDEX_BLOCK]"
                        : $"{candidate.Reason} [NO_MOMENTUM_INDEX_BLOCK]";
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][BLOCK][NO_MOMENTUM_INDEX] symbol={instrument} entryType={candidate.Type}");
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][FILTER][TRANSITION_NO_MOMENTUM] rawTQ={rawTransitionQuality:0.00} tq={transitionQuality:0.00} action=block instrument={instrument} entryType={candidate.Type} momentum={hasMomentum.Value.ToString().ToLowerInvariant()} transition={isTransition.ToString().ToLowerInvariant()}");
                    return false;
                }

                int scoreBeforeIndexPenalty = candidate.Score;
                const int noMomentumIndexPenalty = 10;
                candidate.Score = Math.Max(0, candidate.Score - noMomentumIndexPenalty);
                candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? "[NO_MOMENTUM_INDEX_PENALTY]"
                    : $"{candidate.Reason} [NO_MOMENTUM_INDEX_PENALTY]";
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][TRANSITION_NO_MOMENTUM] rawTQ={rawTransitionQuality:0.00} tq={transitionQuality:0.00} action=penalty instrument={instrument} entryType={candidate.Type} momentum={hasMomentum.Value.ToString().ToLowerInvariant()} transition={isTransition.ToString().ToLowerInvariant()} score={scoreBeforeIndexPenalty}->{candidate.Score} penalty={noMomentumIndexPenalty}");
                return true;
            }

            switch (instrumentClass)
            {
                case InstrumentClass.CRYPTO:
                    block = true;
                    break;
                case InstrumentClass.INDEX:
                    penalty = 8;
                    block = transitionQuality < 0.40;
                    break;
                case InstrumentClass.FX:
                    penalty = 8;
                    break;
                case InstrumentClass.METAL:
                    penalty = 4;
                    break;
            }

            filterApplied = true;

            if (block)
            {
                candidate.IsValid = false;
                candidate.TriggerConfirmed = false;
                candidate.State = EntryState.NONE;
                candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? "[TRANSITION_NO_MOMENTUM_BLOCK]"
                    : $"{candidate.Reason} [TRANSITION_NO_MOMENTUM_BLOCK]";
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][TRANSITION_NO_MOMENTUM] rawTQ={rawTransitionQuality:0.00} tq={transitionQuality:0.00} action=block instrument={instrument} entryType={candidate.Type} momentum={hasMomentum.Value.ToString().ToLowerInvariant()} transition={isTransition.ToString().ToLowerInvariant()}");
                return false;
            }

            int scoreBefore = candidate.Score;
            candidate.Score = Math.Max(0, candidate.Score - penalty);
            candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                ? "[TRANSITION_NO_MOMENTUM_PENALTY]"
                : $"{candidate.Reason} [TRANSITION_NO_MOMENTUM_PENALTY]";
            GlobalLogger.Log(_bot,
                $"[ENTRY][FILTER][TRANSITION_NO_MOMENTUM] rawTQ={rawTransitionQuality:0.00} tq={transitionQuality:0.00} action=penalty instrument={instrument} entryType={candidate.Type} momentum={hasMomentum.Value.ToString().ToLowerInvariant()} transition={isTransition.ToString().ToLowerInvariant()} score={scoreBefore}->{candidate.Score} penalty={penalty}");
            return true;
        }

        private bool ApplyContinuationWeakStructureFilter(EntryContext ctx, EntryEvaluation candidate)
        {
            if (ctx == null || candidate == null)
                return true;

            if (!candidate.IsValid)
            {
                GlobalLogger.Log(_bot,
                    $"[FILTER][SKIPPED_ALREADY_INVALID] symbol={candidate.Symbol ?? ctx.Symbol ?? _bot.SymbolName} entryType={candidate.Type} filter=WEAK_STRUCTURE");
                return true;
            }

            if (!IsStrictContinuationType(candidate.Type))
                return true;

            TransitionEvaluation transition = candidate.Direction == TradeDirection.Long
                ? ctx.TransitionLong
                : candidate.Direction == TradeDirection.Short
                    ? ctx.TransitionShort
                    : null;
            string instrument = candidate.Symbol ?? ctx.Symbol ?? _bot.SymbolName;

            if (transition == null)
            {
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][WEAK_STRUCTURE] instrument={instrument} entryType={candidate.Type} flagQuality=missing impulseStrength=missing action=none");
                return true;
            }

            double flagQuality = transition.CompressionScore;
            double impulseStrength = transition.QualityScore01;
            var instrumentClass = SymbolRouting.ResolveInstrumentClass(instrument);

            double qualityThreshold = 0.35;
            double flagThreshold = 0.25;
            int penalty = 3;
            bool block = false;

            switch (instrumentClass)
            {
                case InstrumentClass.CRYPTO:
                    qualityThreshold = 0.55;
                    flagThreshold = 0.45;
                    block = true;
                    break;
                case InstrumentClass.INDEX:
                    qualityThreshold = 0.45;
                    flagThreshold = 0.35;
                    penalty = 6;
                    break;
                case InstrumentClass.METAL:
                    qualityThreshold = 0.40;
                    flagThreshold = 0.30;
                    penalty = 2;
                    break;
            }

            if (IsFlagEntryType(candidate.Type))
            {
                int flagBars = GetFlagBarsForCandidate(ctx, candidate, transition);
                bool hasValidFlagContext = transition.HasFlag ||
                                           transition.FlagBars > 0 ||
                                           (ctx.FlagHigh > ctx.FlagLow) ||
                                           ctx.FlagAtr_M5 > 0 ||
                                           Math.Max(ctx.FlagBarsLong_M5, ctx.FlagBarsShort_M5) > 0;
                if (flagBars < 2)
                {
                    bool shouldHardBlock = flagBars == 0 || !hasValidFlagContext;
                    GlobalLogger.Log(_bot,
                        $"[INDEX][FLAG_CHECK] flagBars={flagBars} action={(shouldHardBlock ? "block" : "penalty")}");

                    if (shouldHardBlock)
                    {
                        candidate.IsValid = false;
                        candidate.TriggerConfirmed = false;
                        candidate.State = EntryState.NONE;
                        candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                            ? "[INVALID_FLAG]"
                            : $"{candidate.Reason} [INVALID_FLAG]";
                        GlobalLogger.Log(_bot,
                            $"[ENTRY][BLOCK][INVALID_FLAG] symbol={instrument} flagBars={flagBars} entryType={candidate.Type}");
                        return false;
                    }

                    int flagPenaltyBefore = candidate.Score;
                    const int weakFlagPenalty = 8;
                    candidate.Score = Math.Max(0, candidate.Score - weakFlagPenalty);
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? "[WEAK_FLAG_STRUCTURE]"
                        : $"{candidate.Reason} [WEAK_FLAG_STRUCTURE]";
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][FILTER][INVALID_FLAG] symbol={instrument} flagBars={flagBars} entryType={candidate.Type} action=penalty score={flagPenaltyBefore}->{candidate.Score} penalty={weakFlagPenalty}");
                }
            }

            if (!transition.HasImpulse)
            {
                if (instrumentClass == InstrumentClass.CRYPTO)
                {
                    candidate.IsValid = false;
                    candidate.TriggerConfirmed = false;
                    candidate.State = EntryState.NONE;
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? "[NO_IMPULSE_BLOCK]"
                        : $"{candidate.Reason} [NO_IMPULSE_BLOCK]";
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][FILTER][NO_IMPULSE] instrument={instrumentClass} entryType={candidate.Type} action=block");
                    return false;
                }

                if (instrumentClass == InstrumentClass.INDEX)
                {
                    GlobalLogger.Log(_bot,
                        $"[ENTRY][FILTER][NO_IMPULSE] instrument={instrumentClass} entryType={candidate.Type} action=none");
                    return true;
                }

                int impulseScoreBefore = candidate.Score;
                candidate.Score = Math.Max(0, candidate.Score - penalty);
                candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? "[NO_IMPULSE_PENALTY]"
                    : $"{candidate.Reason} [NO_IMPULSE_PENALTY]";
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][NO_IMPULSE] instrument={instrumentClass} entryType={candidate.Type} action=penalty score={impulseScoreBefore}->{candidate.Score} penalty={penalty}");
                return true;
            }

            bool weakStructure =
                impulseStrength < qualityThreshold ||
                flagQuality < flagThreshold;

            if (!weakStructure)
            {
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][WEAK_STRUCTURE] instrument={instrument} entryType={candidate.Type} flagQuality={flagQuality:0.00} impulseStrength={impulseStrength:0.00} action=none");
                return true;
            }

            if (block)
            {
                candidate.IsValid = false;
                candidate.TriggerConfirmed = false;
                candidate.State = EntryState.NONE;
                candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? "[WEAK_STRUCTURE_BLOCK]"
                    : $"{candidate.Reason} [WEAK_STRUCTURE_BLOCK]";
                GlobalLogger.Log(_bot,
                    $"[ENTRY][FILTER][WEAK_STRUCTURE] instrument={instrument} entryType={candidate.Type} flagQuality={flagQuality:0.00} impulseStrength={impulseStrength:0.00} action=block");
                return false;
            }

            int scoreBefore = candidate.Score;
            candidate.Score = Math.Max(0, candidate.Score - penalty);
            candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                ? "[WEAK_STRUCTURE_PENALTY]"
                : $"{candidate.Reason} [WEAK_STRUCTURE_PENALTY]";
            GlobalLogger.Log(_bot,
                $"[ENTRY][FILTER][WEAK_STRUCTURE] instrument={instrument} entryType={candidate.Type} flagQuality={flagQuality:0.00} impulseStrength={impulseStrength:0.00} action=penalty score={scoreBefore}->{candidate.Score} penalty={penalty}");
            return true;
        }

        private static bool IsContinuationMomentumType(EntryType type)
        {
            switch (type)
            {
                case EntryType.Index_Flag:
                case EntryType.Index_Breakout:
                case EntryType.FX_FlagContinuation:
                case EntryType.FX_MicroContinuation:
                case EntryType.FX_ImpulseContinuation:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsFlagEntryType(EntryType type)
        {
            string typeName = type.ToString();
            return typeName.IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetFlagBarsForCandidate(EntryContext ctx, EntryEvaluation candidate, TransitionEvaluation transition)
        {
            if (transition != null && transition.FlagBars > 0)
                return transition.FlagBars;

            if (candidate.Direction == TradeDirection.Long)
                return ctx.FlagBarsLong_M5;
            if (candidate.Direction == TradeDirection.Short)
                return ctx.FlagBarsShort_M5;

            return Math.Max(ctx.FlagBarsLong_M5, ctx.FlagBarsShort_M5);
        }

        private static bool IsStrictContinuationType(EntryType type)
        {
            string typeName = type.ToString();
            return typeName.IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Pullback", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Breakout", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldRejectEarlyNoStructure(
            EntryContext ctx,
            EntryEvaluation candidate,
            bool hasStrongTrigger)
        {
            if (ctx == null || candidate == null || candidate.Direction == TradeDirection.None)
                return false;

            if (!IsContinuationEarlyValidityType(candidate.Type))
                return false;

            bool isEarly =
                (candidate.Direction == TradeDirection.Long && ctx.HasEarlyContinuationLong) ||
                (candidate.Direction == TradeDirection.Short && ctx.HasEarlyContinuationShort);
            MovePhase movePhase;
            if (ctx.MovePhase != MovePhase.Unknown)
            {
                movePhase = ctx.MovePhase;
            }
            else if (ctx.MemoryState != null)
            {
                movePhase = ctx.MemoryState.MovePhase;
            }
            else
            {
                movePhase = MovePhase.Unknown;
            }
            bool isImpulsePhase = movePhase == MovePhase.Impulse;

            bool hasPullback = ctx.BarsSinceFirstPullback >= 0;
            bool hasMinimalStructure = hasPullback;

            return isEarly &&
                   isImpulsePhase &&
                   !hasMinimalStructure &&
                   !hasStrongTrigger;
        }

        private static bool IsContinuationEarlyValidityType(EntryType type)
        {
            string typeName = type.ToString();
            return typeName.IndexOf("Continuation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("MicroStructure", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetBarsSinceBreak(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null)
                return int.MaxValue;

            return direction == TradeDirection.Long
                ? ctx.BarsSinceHighBreak_M5
                : direction == TradeDirection.Short
                    ? ctx.BarsSinceLowBreak_M5
                    : int.MaxValue;
        }

        private static bool IsCryptoCandidate(EntryType type)
        {
            switch (type)
            {
                case EntryType.Crypto_Flag:
                case EntryType.Crypto_Pullback:
                case EntryType.Crypto_RangeBreakout:
                case EntryType.Crypto_Impulse:
                    return true;
                default:
                    return false;
            }
        }

        private bool PassFinalAcceptance(EntryContext ctx, EntryEvaluation eval)
        {
            if (ctx == null || eval == null)
                return false;

            if (eval.Direction == TradeDirection.None)
            {
                GlobalLogger.Log(_bot, "[FA][INTEGRITY_BLOCK] reason=DIRECTION_NONE");
                GlobalLogger.Log(_bot, "[FINAL][DECISION] decision=BLOCK reason=DIRECTION_NONE");
                return false;
            }

            if (BotRestartState.IsHardProtectionPhase && eval.Score < 60)
            {
                GlobalLogger.Log(_bot, "[FA][INTEGRITY_BLOCK] reason=RESTART_HARD_PROTECTION");
                GlobalLogger.Log(_bot, "[FINAL][DECISION] decision=BLOCK reason=RESTART");
                return false;
            }

            bool timingStaleOrExpired =
                ctx.MemoryContinuationWindow == ContinuationWindowState.Unknown ||
                ctx.MemoryContinuationWindow == ContinuationWindowState.Exhausted;
            if (timingStaleOrExpired)
            {
                GlobalLogger.Log(_bot, "[FA][INTEGRITY_BLOCK] reason=TIMING_STALE_OR_EXPIRED");
                GlobalLogger.Log(_bot, "[FINAL][DECISION] decision=BLOCK reason=TIMING");
                return false;
            }

            if (ctx.IsOverextendedLong || ctx.IsOverextendedShort)
            {
                GlobalLogger.Log(_bot, "[FA][INTEGRITY_BLOCK] reason=OVEREXTENDED");
                GlobalLogger.Log(_bot, "[FINAL][DECISION] decision=BLOCK reason=OVEREXTENDED");
                return false;
            }

            GlobalLogger.Log(_bot, "[FINAL][DECISION] decision=ALLOW reason=PASS");
            return true;
        }

        private double ResolveExecutionRiskPercent(EntryEvaluation selected, EntryContext ctx)
        {
            if (selected == null || ctx == null)
                return 0;

            int logic = PositionContext.ClampRiskConfidence(Math.Max(0, ctx.LogicBiasConfidence));
            int entryScore = PositionContext.ClampRiskConfidence(selected.Score);
            int final = PositionContext.ComputeFinalConfidenceValue(entryScore, logic);
            int adjusted = PositionContext.ClampRiskConfidence(final);
            string symbol = (ctx.Symbol ?? _bot.SymbolName ?? string.Empty).ToUpperInvariant();

            if (symbol == "BTCUSD")
                return _btcUsdRiskSizer?.GetRiskPercent(adjusted) ?? 0;
            if (symbol == "ETHUSD")
                return _ethUsdRiskSizer?.GetRiskPercent(adjusted) ?? 0;

            return 0;
        }

        private static bool HasStrongStructure(EntryContext ctx, TradeDirection direction)
        {
            if (ctx == null)
                return false;

            switch (direction)
            {
                case TradeDirection.Long:
                    return ctx.BrokeLastSwingHigh_M5 ||
                           ctx.FlagBreakoutUpConfirmed ||
                           (ctx.HasBreakout_M1 && ctx.BreakoutDirection == TradeDirection.Long);

                case TradeDirection.Short:
                    return ctx.BrokeLastSwingLow_M5 ||
                           ctx.FlagBreakoutDownConfirmed ||
                           (ctx.HasBreakout_M1 && ctx.BreakoutDirection == TradeDirection.Short);

                default:
                    return false;
            }
        }

        private void UpsertArmedSetup(EntryEvaluation candidate, int barsSinceBreak, bool triggerConfirmed, int currentBarIndex)
        {
            if (candidate == null)
                return;

            string key = GetArmedSetupKey(candidate);
            _armedSetups.TryGetValue(key, out var existing);
            int triggerBarIndex = -1;
            if (triggerConfirmed)
            {
                triggerBarIndex = existing?.TriggerBarIndex >= 0
                    ? existing.TriggerBarIndex
                    : currentBarIndex;
            }
            _armedSetups[key] = new ArmedSetup
            {
                Symbol = candidate.Symbol ?? _bot.SymbolName,
                Direction = candidate.Direction,
                Score = candidate.Score,
                DetectedAt = _bot.Server.Time,
                BarsSince = barsSinceBreak,
                TriggerBarIndex = triggerBarIndex,
                EntryType = candidate.Type,
                Reason = candidate.Reason ?? string.Empty
            };
        }

        private void ClearArmedSetup(EntryEvaluation candidate)
        {
            if (candidate == null)
                return;

            _armedSetups.Remove(GetArmedSetupKey(candidate));
        }

        private static string GetArmedSetupKey(EntryEvaluation candidate)
        {
            return $"{candidate?.Symbol}:{candidate?.Type}:{candidate?.Direction}";
        }

        private void LogEntryExecuted(EntryEvaluation candidate)
        {
            if (candidate == null)
                return;

            ClearArmedSetup(candidate);
            GlobalLogger.Log(_bot, $"[ENTRY EXECUTED] symbol={candidate.Symbol ?? _bot.SymbolName} score={candidate.Score} type={candidate.Type} dir={candidate.Direction}");
        }

        private sealed class TriggerDiagnostics
        {
            public bool IsManaged { get; set; }
            public bool TriggerConfirmed { get; set; }
            public bool BreakoutClose { get; set; }
            public bool StructureBreak { get; set; }
            public bool M1Break { get; set; }
            public string WaitReason { get; set; } = string.Empty;
        }

        private sealed class EntryTraceSummary
        {
            public int TotalEvaluations { get; set; }
            public int LogicProducedDirectionCount { get; set; }
            public int LostAfterScoreCount { get; set; }
            public int LostAfterGateCount { get; set; }
            public int NeverHadDirectionCount { get; set; }
        }

        private string _lastOnTickStage = "INIT";
        
        // =========================================================
        // TICK-LEVEL EXIT DISPATCH (isolated & safe)
        // =========================================================
        public void OnTick()
        {
            EnsureRuntimeResolverInitialized();
            _runtimeSymbols.BeginExecutionCycle();
            try
            {
                // =====================================================
                // HARD LOSS GUARD – GLOBAL SAFETY
                // =====================================================
                if (CheckHardLoss())
                {
                    GlobalLogger.Log(_bot, "BLOCK: hard loss guard");
                    return;
                }

                if (IsSymbol("XAUUSD"))
                {
                    DispatchExitManagerOnTick(_xauExitManager, () => _xauExitManager?.OnTick());
                }
                else if (IsNasSymbol(_bot.SymbolName))
                {
                    DispatchExitManagerOnTick(_nasExitManager, () => _nasExitManager?.OnTick());
                }
                else if (IsSymbol("US30"))
                {
                    DispatchExitManagerOnTick(_us30ExitManager, () => _us30ExitManager?.OnTick());
                }
                else if (IsSymbol("GER40"))
                {
                    DispatchExitManagerOnTick(_ger40ExitManager, () => _ger40ExitManager?.OnTick());
                }
                else if (IsSymbol("EURUSD"))
                {
                    DispatchExitManagerOnTick(_eurUsdExitManager, () => _eurUsdExitManager?.OnTick());
                }
                else if (IsSymbol("USDJPY"))
                {
                    DispatchExitManagerOnTick(_usdJpyExitManager, () => _usdJpyExitManager?.OnTick());
                }
                else if (IsSymbol("GBPUSD"))
                {
                    DispatchExitManagerOnTick(_gbpUsdExitManager, () => _gbpUsdExitManager?.OnTick());
                }
                else if (IsSymbol("AUDUSD"))
                {
                    DispatchExitManagerOnTick(_audUsdExitManager, () => _audUsdExitManager?.OnTick());
                }
                else if (IsSymbol("AUDNZD"))
                {
                    DispatchExitManagerOnTick(_audNzdExitManager, () => _audNzdExitManager?.OnTick());
                }
                else if (IsSymbol("EURJPY"))
                {
                    DispatchExitManagerOnTick(_eurJpyExitManager, () => _eurJpyExitManager?.OnTick());
                }
                else if (IsSymbol("GBPJPY"))
                {
                    DispatchExitManagerOnTick(_gbpJpyExitManager, () => _gbpJpyExitManager?.OnTick());
                }
                else if (IsSymbol("NZDUSD"))
                {
                    DispatchExitManagerOnTick(_nzdUsdExitManager, () => _nzdUsdExitManager?.OnTick());
                }
                else if (IsSymbol("USDCAD"))
                {
                    DispatchExitManagerOnTick(_usdCadExitManager, () => _usdCadExitManager?.OnTick());
                }
                else if (IsSymbol("USDCHF"))
                {
                    DispatchExitManagerOnTick(_usdChfExitManager, () => _usdChfExitManager?.OnTick());
                }
                else if (IsSymbol("BTCUSD"))
                {
                    DispatchExitManagerOnTick(_btcUsdExitManager, () => _btcUsdExitManager?.OnTick());
                }
                else if (IsSymbol("ETHUSD"))
                {
                    DispatchExitManagerOnTick(_ethUsdExitManager, () => _ethUsdExitManager?.OnTick());
                }
            }
            catch (Exception ex)
            {
                GlobalLogger.Log(_bot, 
                    $"[AUDIT][ONTICK EXCEPTION] symbol={_bot.SymbolName} type={ex.GetType().Name} " +
                    $"message={ex.Message} positionCount={_bot.Positions.Count} contextCount={GetTrackedContextCount()} " +
                    $"lastStage={_lastOnTickStage}");
                GlobalLogger.Log(_bot, $"[TC][ONTICK][FATAL] {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private object _lastOnTickManager;
        private DateTime _lastTickLogTime = DateTime.MinValue;

        private void DispatchExitManagerOnTick(object manager, Action onTickAction)
        {
            _lastOnTickManager = manager;
            _lastOnTickStage = manager?.GetType().Name ?? _bot.SymbolName;

            onTickAction?.Invoke();
        }

        private int GetTrackedContextCount()
        {
            if (_lastOnTickManager == null)
                return 0;

            var field = _lastOnTickManager.GetType().GetField("_contexts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(_lastOnTickManager) is System.Collections.IDictionary dictionary)
                return dictionary.Count;

            return 0;
        }

        private static TradeType ToTradeTypeStrict(TradeDirection d)
        {
            if (d == TradeDirection.Long) return TradeType.Buy;
            if (d == TradeDirection.Short) return TradeType.Sell;
            throw new ArgumentException("TradeDirection.None is not allowed");
        }
        private TradeDirection ResolveHtfAllowedDirection(EntryContext ctx)
        {
            return ctx?.ActiveHtfDirection ?? TradeDirection.None;
        }

        private double ResolveHtfConfidence(EntryContext ctx)
        {
            return ctx?.ActiveHtfConfidence ?? 0.0;
        }

        private string ResolveHtfState(EntryContext ctx)
        {
            if (ctx == null)
                return "N/A";

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(SymbolRouting.NormalizeSymbol(ctx.Symbol));
            if (instrumentClass == InstrumentClass.FX) return ctx.FxHtfReason ?? "N/A";
            if (instrumentClass == InstrumentClass.CRYPTO) return ctx.CryptoHtfReason ?? "N/A";
            if (instrumentClass == InstrumentClass.METAL) return ctx.MetalHtfReason ?? "N/A";
            if (instrumentClass == InstrumentClass.INDEX) return ctx.IndexHtfReason ?? "N/A";
            return "N/A";
        }

        private void LogHtfFlowStage(EntryContext ctx, EntryEvaluation candidate, string stageName, string module)
        {
            if (ctx == null || candidate == null)
                return;

            var allowed = ResolveHtfAllowedDirection(ctx);
            string state = ResolveHtfState(ctx);
            string asset = SymbolRouting.ResolveInstrumentClass(SymbolRouting.NormalizeSymbol(ctx.Symbol)).ToString();
            bool align = candidate.Direction != TradeDirection.None && (allowed == TradeDirection.None || allowed == candidate.Direction);

            GlobalLogger.Log(_bot, 
                $"[AUDIT][HTF FLOW][{stageName}] symbol={ctx.Symbol} asset={asset} entryType={candidate.Type} stage={stageName} module={module} " +
                $"htfState={state} allowedDirection={allowed} align={align} candidateDirection={candidate.Direction}");


            string reason = $"{candidate.Reason} {candidate.RejectReason}";
            if (!string.IsNullOrWhiteSpace(reason) && reason.Contains("HTF_"))
            {
                EnsureHtfClassification(ctx, candidate);
                string htfClassification = candidate.HtfClassification ?? "HTF_NO_DIRECTION";
                GlobalLogger.Log(_bot, 
                    $"[AUDIT][HTF REJECT ANALYSIS] symbol={ctx.Symbol} asset={asset} entryType={candidate.Type} candidateDirection={candidate.Direction} " +
                    $"htfAllowedDirection={allowed} htfState={state} align={align} trueDirectionMismatch={(candidate.Direction != TradeDirection.None && allowed != TradeDirection.None && candidate.Direction != allowed ? "YES" : "NO")} " +
                    $"classification={htfClassification} rejectModule={module}");
            }
        }

        private void StampEntrySourceHtfTrace(EntryContext ctx, EntryEvaluation eval)
        {
            if (ctx == null || eval == null)
                return;

            TradeDirection allowedDirection = ResolveHtfAllowedDirection(ctx);
            TradeDirection candidateDirection = eval.Direction;
            bool align = candidateDirection != TradeDirection.None
                && (allowedDirection == TradeDirection.None || allowedDirection == candidateDirection);

            eval.HtfTraceSourceStage = "ENTRY_SOURCE";
            eval.HtfTraceSourceModule = eval.Type.ToString();
            eval.HtfTraceSourceState = ResolveHtfState(ctx);
            eval.HtfTraceSourceAllowedDirection = allowedDirection;
            eval.HtfTraceSourceAlign = align;
            eval.HtfTraceSourceCandidateDirection = candidateDirection;
            eval.HtfConfidence01 = ResolveHtfConfidence(ctx);
            HtfClassificationModel.InitializeEntryHtfClassification(eval, candidateDirection, allowedDirection);
        }

        private static TradeDirection FromTradeType(TradeType tradeType)
        {
            return tradeType == TradeType.Buy
                ? TradeDirection.Long
                : TradeDirection.Short;
        }

        private bool HasOpenGeminiPosition()
        {
            foreach (var p in _bot.Positions)
            {
                if (p == null) continue;
                if (p.Label != BotLabel) continue;
                if (p.SymbolName != _bot.SymbolName) continue;
                return true;
            }
            return false;
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            if (pos == null) return;

            // 🔒 csak saját bot
            if (pos.Label != BotLabel)
                return;

            // 🔒 csak saját symbol
            if (!pos.SymbolName.Equals(_bot.SymbolName, StringComparison.OrdinalIgnoreCase))
                return;

            _tradeMetaStore.TryGet(pos.Id, out var meta);
            _positionContexts.TryGetValue(pos.Id, out var ctx);
            var entryCtx = _contextRegistry.GetEntry(pos.Id);
            double entryBalance = _entryBalanceByPositionId.TryGetValue(pos.Id, out var mappedBalance)
                ? mappedBalance
                : _bot.Account.Balance;

            if (meta == null)
            {
                GlobalLogger.Log(_bot, 
                    $"[META MISSING] pos={pos.Id} symbol={pos.SymbolName}"
                );
            }

            string exitMode = args.Reason.ToString();

            if (ctx != null)
            {
                if (ctx.GetTp1SmartExitHit())
                    exitMode = "TP1_SMART";
                else if (ctx.Tp2Hit > 0)
                    exitMode = "TP2";
                else if (ctx.TrailingActivated)
                    exitMode = "TRAIL";
                else if (ctx.BeActivated)
                    exitMode = "BE";
                else if (ctx.Tp1Hit)
                    exitMode = "TP1";
                else
                    exitMode = "SL";
            }

            var sym = _runtimeSymbols.ResolveSymbol(pos.SymbolName);
            double pipSize = sym?.PipSize ?? (ctx?.PipSize > 0 ? ctx.PipSize : 0);
            double exitPrice = pipSize > 0
                ? pos.EntryPrice + pos.Pips * pipSize * (pos.TradeType == TradeType.Buy ? 1 : -1)
                : pos.EntryPrice;

            if (pipSize <= 0)
            {
                GlobalLogger.Log(_bot, $"[EXIT][WARN] pos={pos.Id} symbol={pos.SymbolName} reason=missing_runtime_pipsize");
            }

            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                $"[EXIT][BROKER_CLOSE_DETECTED]\nreason={MapBrokerCloseReason(args.Reason)}",
                ctx,
                pos));
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(
                TradeAuditLog.BuildExitSnapshot(
                    ctx,
                    pos,
                    args.Reason.ToString(),
                    _bot.Server.Time,
                    exitPrice),
                ctx,
                pos));
            if (ctx != null)
            {
                GlobalLogger.Log(_bot, $"[MFE_CLOSE] finalMFE={ctx.MfeR} finalMAE={ctx.MaeR}");
                GlobalLogger.Log(_bot, $"[CLOSE] final MFE={ctx.MfeR} MAE={ctx.MaeR}");
            }
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds($"[EXIT][DECISION]\nreason={args.Reason}\ndetail=broker_closed_event", ctx, pos));

            _logger.OnTradeClosed(
                BuildLogContext(pos, meta, ctx, entryCtx),
                pos,
                new TradeLogResult
                {
                    ExitMode = exitMode,
                    ExitReason = ctx?.GetTp1SmartExitHit() == true
                        ? (string.IsNullOrWhiteSpace(ctx.GetTp1SmartExitReason()) ? "TREND_COLLAPSE" : ctx.GetTp1SmartExitReason())
                        : args.Reason.ToString(),
                    ExitTimeUtc = _bot.Server.Time,
                    ExitPrice = exitPrice,
                    NetProfit = pos.NetProfit,
                    GrossProfit = pos.GrossProfit,
                    Commissions = pos.Commissions,
                    Swap = pos.Swap,
                    Pips = pos.Pips,
                    PostTp1MaxR = ctx?.PostTp1MaxR,
                    PostTp1GivebackR = ctx?.PostTp1GivebackR,
                    Tp1ProtectExitHit = ctx?.Tp1ProtectExitHit,
                    Tp1ProtectExitR = ctx?.Tp1ProtectExitR,
                    Tp1ProtectScoreAtExit = ctx?.Tp1ProtectScoreAtExit,
                    Tp1ProtectMode = ctx?.Tp1ProtectMode,
                    Tp1SmartExitHit = ctx?.GetTp1SmartExitHit(),
                    Tp1SmartExitType = ctx?.GetTp1SmartExitType(),
                    Tp1SmartExitReason = ctx?.GetTp1SmartExitReason(),
                    Tp1SmartExitR = ctx?.GetTp1SmartExitR(),
                    Tp1SmartBarsSinceTp1 = ctx?.GetTp1SmartBarsSinceTp1()
                });

            _statsTracker.RegisterTradeClose(
                pos.Id,
                entryCtx,
                pos.NetProfit,
                new TradeStatsTracker.TradeCloseSnapshot
                {
                    TimestampUtc = _bot.Server.Time.ToUniversalTime(),
                    Symbol = pos.SymbolName,
                    PositionId = (ctx != null && ctx.PositionId > 0 ? ctx.PositionId : pos.Id).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Direction = pos.TradeType.ToString(),
                    EntryPrice = pos.EntryPrice,
                    ExitPrice = pos.EntryPrice + pos.Pips * sym.PipSize * (pos.TradeType == TradeType.Buy ? 1 : -1),
                    Profit = pos.NetProfit,
                    OpenTimeUtc = pos.EntryTime.ToUniversalTime(),
                    CloseTimeUtc = _bot.Server.Time.ToUniversalTime(),
                    Score = null,
                    Confidence = ctx?.FinalConfidence ?? meta?.EntryScore,
                    SetupType = ResolveSetupType(meta?.EntryType),
                    EntryType = meta?.EntryType ?? ctx?.EntryType ?? "UNKNOWN",
                    MarketRegime = ResolveMarketRegime(entryCtx),
                    MfeR = ctx?.MfeR ?? 0.0,
                    MaeR = ctx != null ? -Math.Abs(ctx.MaeR) : 0.0,
                    RMultiple = ComputeRMultiple(pos, ctx, sym.PipSize),
                    TransitionQuality = entryCtx?.Transition?.QualityScore01 ?? 0.0,
                    AccountBalanceAtEntry = entryBalance
                });

            LogScalingCloseAudit(pos, ctx, entryBalance);

            var memoryRecord = new TradeMemoryRecord
            {
                Instrument = pos.SymbolName ?? string.Empty,
                EntryType = meta?.EntryType ?? ctx?.EntryType ?? string.Empty,
                SetupType = ResolveSetupType(meta?.EntryType ?? ctx?.EntryType),
                Direction = pos.TradeType == TradeType.Buy ? "Long" : "Short",
                RMultiple = ComputeRMultiple(pos, ctx, sym.PipSize),
                MFE = ctx?.MfeR ?? 0.0,
                MAE = ctx != null ? -Math.Abs(ctx.MaeR) : 0.0,
                MarketRegime = ResolveMarketRegime(entryCtx),
                TransitionQuality = entryCtx?.Transition?.QualityScore01 ?? 0.0,
                Confidence = ctx?.FinalConfidence ?? meta?.EntryScore ?? 0.0,
                EntryTime = ctx?.EntryTime ?? pos.EntryTime,
                ExitTime = _bot.Server.Time
            };

            _tradeMemoryStore.AddRecord(memoryRecord);
            _memoryLogger.LogWrite(memoryRecord);

            _positionContexts.Remove(pos.Id);
            _contextRegistry.RemovePosition(pos.Id);
            _contextRegistry.RemoveEntry(pos.Id);
            _tradeMetaStore.Remove(pos.Id);
            _entryBalanceByPositionId.Remove(pos.Id);
            GlobalLogger.Log(_bot, TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildCleanup(pos.Id, "position_closed_event"), ctx, pos));
        }

        private static string MapBrokerCloseReason(PositionCloseReason reason)
        {
            return reason == PositionCloseReason.StopLoss
                ? "STOP_LOSS"
                : reason == PositionCloseReason.TakeProfit
                    ? "TAKE_PROFIT"
                    : "UNKNOWN";
        }

        private TradeLogContext BuildLogContext(Position pos, PendingEntryMeta meta, PositionContext pctx = null, EntryContext ectx = null)
        {
            return new TradeLogContext
            {
                TimestampUtc = _bot.Server.Time.ToUniversalTime(),
                Symbol = pos?.SymbolName ?? _bot.SymbolName,
                Direction = pos?.TradeType.ToString(),
                StrategyVersion = "GeminiV26",
                PositionId = pos?.Id,
                TradeId = pos?.Id.ToString(),
                PositionContext = pctx,
                EntryContext = ectx ?? (pos != null ? _contextRegistry.GetEntry(pos.Id) : null),
                PendingMeta = meta
            };
        }

        public void OnStop()
        {
            _bot.Positions.Closed -= OnPositionClosed;
            _logWriter?.Dispose();
        }

        public void RehydrateOpenPositions()
        {
            EnsureRuntimeResolverInitialized();
            EnsureStartupMemoryReady();
            AuditMemoryCoverage();
            AuditResolverCoverage();

            if (!_isMemoryReady)
            {
                GlobalLogger.Log(_bot, "[BOOT][REHYDRATE_BLOCKED] reason=memory_not_ready");
                return;
            }

            GlobalLogger.Log(_bot, "[BOOT][REHYDRATE_START]");
            var service = new RehydrateService(
                _bot,
                _positionContexts,
                _contextRegistry,
                BotLabel,
                RegisterRehydratedContextWithExitManager,
                _memoryEngine);

            var summary = service.Run();
            int openCount = summary?.TotalOpenPositionsSeen ?? 0;
            int restored = summary?.SuccessfullyRehydrated ?? 0;
            int skipped = summary?.Skipped ?? 0;
            int failed = summary?.Failed ?? 0;
            GlobalLogger.Log(_bot, $"[BOOT][REHYDRATE_DONE] restored={restored} skipped={skipped} failed={failed} openPositions={openCount}");
        }

        // =================================================
        // SYMBOL NORMALIZATION
        // =================================================
        private static string NormalizeSymbol(string symbol)
        {
            return SymbolRouting.NormalizeSymbol(symbol);
        }

        private void AuditMemoryCoverage()
        {
            var symbols = GetTrackedCanonicalSymbols();
            bool isStartupWindow = BotRestartState.BarsSinceStart <= 5;

            foreach (var symbol in symbols)
            {
                SymbolMemoryState memoryState = _memoryEngine.GetState(symbol);
                bool isNull = memoryState == null;
                bool isBuilt = memoryState?.IsBuilt == true;
                bool isResolved = memoryState?.IsResolved == true;
                bool isUsable = memoryState?.IsUsable == true;

                if (isNull || !isBuilt)
                {
                    memoryState = RecoverMissingMemoryState(symbol, memoryState, "audit");
                    isNull = memoryState == null;
                    isBuilt = memoryState?.IsBuilt == true;
                    isResolved = memoryState?.IsResolved == true;
                    isUsable = memoryState?.IsUsable == true;

                    if (isBuilt)
                        continue;

                    GlobalLogger.Log(_bot, $"[MEMORY][MISSING] symbol={symbol} built={isBuilt} stateNull={isNull}");

                    if (isStartupWindow)
                        GlobalLogger.Log(_bot, $"[MEMORY][CRITICAL] symbol={symbol} missing_after_startup");

                    continue;
                }

                if (!isResolved)
                {
                    GlobalLogger.Log(_bot, $"[MEMORY][MISSING] symbol={symbol} built={isBuilt} resolved={isResolved} usable={isUsable} reason={memoryState?.ResolveFailureReason ?? string.Empty}");
                    continue;
                }

                if (MarketMemoryEngine.DebugMemory)
                {
                    GlobalLogger.Log(_bot, 
                        $"[DEBUG][MEMORY][OK] symbol={symbol} phase={memoryState.MovePhase} age={memoryState.MoveAgeBars} pullbacks={memoryState.PullbackCount} usable={isUsable}");
                }
            }

            EmitStartupCoverageLogs(symbols);
        }

        private void EmitStartupCoverageLogs(List<string> symbols)
        {
            if (_startupCoverageLogged)
                return;

            GlobalLogger.Log(_bot, $"[MEMORY][COVERAGE] built={_memoryEngine.GetBuiltCoverageRatio(symbols)}");
            GlobalLogger.Log(_bot, $"[MEMORY][RESOLVE_COVERAGE] resolved={_memoryEngine.GetResolvedCoverageRatio(symbols)}");
            GlobalLogger.Log(_bot, $"[MEMORY][USABLE_COVERAGE] usable={_memoryEngine.GetUsableCoverageRatio(symbols)}");
            _startupCoverageLogged = true;
        }

        private void AuditResolverCoverage()
        {
            var symbols = GetTrackedCanonicalSymbols();
            int resolved = 0;
            int total = 0;
            bool isStartupWindow = BotRestartState.BarsSinceStart <= 5;

            foreach (var symbol in symbols)
            {
                total++;
                if (_runtimeSymbols.TryResolveSymbol(symbol, out _))
                {
                    resolved++;
                    continue;
                }

                GlobalLogger.Log(_bot, $"[RESOLVER][MISSING] symbol={symbol}");
                if (isStartupWindow)
                    GlobalLogger.Log(_bot, $"[RESOLVER][CRITICAL] symbol={symbol} missing_after_startup");
            }

            GlobalLogger.Log(_bot, $"[RESOLVER][VALIDATION] resolved={resolved}/{total}");
            if (resolved < total)
                GlobalLogger.Log(_bot, $"[RESOLVER][ERROR] validation_failed resolved={resolved}/{total}");

            GlobalLogger.Log(_bot, $"[RESOLVER][COVERAGE] resolved={resolved}/{total}");
        }

        private List<string> GetTrackedCanonicalSymbols()
        {
            var canonicalSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string symbolName = _bot.Symbol?.Name;
            string chartSymbolName = _bot.SymbolName;

            if (!string.IsNullOrWhiteSpace(symbolName))
                canonicalSymbols.Add(NormalizeSymbol(symbolName));

            if (!string.IsNullOrWhiteSpace(chartSymbolName))
                canonicalSymbols.Add(NormalizeSymbol(chartSymbolName));

            return canonicalSymbols
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private bool RegisterRehydratedContextWithExitManager(PositionContext ctx)
        {
            if (ctx == null)
                return false;

            string canonical = NormalizeSymbol(ctx.Symbol);
            var symbol = ResolveSymbol(canonical);
            if (!IsTradable(symbol))
            {
                GlobalLogger.Log(_bot, $"[REHYDRATE_WARN] pos={ctx.PositionId} symbol={ctx.Symbol} reason=symbol_not_tradable");
                return false;
            }

            var manager = GetExitManager(canonical);
            if (manager != null)
            {
                manager.RegisterContext(ctx);
                return true;
            }

            GlobalLogger.Log(_bot, $"[REHYDRATE_WARN] pos={ctx.PositionId} symbol={ctx.Symbol} reason=no_exit_manager_for_symbol");
            return false;
        }

        private void RegisterExitManager(string canonical, IExitManager manager)
        {
            if (string.IsNullOrWhiteSpace(canonical) || manager == null)
                return;

            _exitManagersByCanonical[NormalizeSymbol(canonical)] = manager;
        }

        private IExitManager GetExitManager(string canonical)
        {
            if (string.IsNullOrWhiteSpace(canonical))
                return null;

            _exitManagersByCanonical.TryGetValue(NormalizeSymbol(canonical), out var manager);
            return manager;
        }

        private Symbol ResolveSymbol(string canonical)
        {
            return string.IsNullOrWhiteSpace(canonical)
                ? null
                : _runtimeSymbols.ResolveSymbol(NormalizeSymbol(canonical));
        }

        private static bool IsTradable(Symbol symbol)
        {
            return symbol != null && symbol.Bid != 0 && symbol.Ask != 0;
        }

        // =================================================
        // INDEX HELPERS
        // =================================================
        private static bool IsNasSymbol(string symbol)
        {
            return SymbolRouting.NormalizeSymbol(symbol) == "NAS100";
        }

        private static bool IsUs30(string symbol)
        {
            return SymbolRouting.NormalizeSymbol(symbol) == "US30";
        }

        private static bool IsGer40(string symbol)
        {
            return SymbolRouting.NormalizeSymbol(symbol) == "GER40";
        }


        private static string ResolveInstrumentClass(string symbol)
        {
            return SymbolRouting.ResolveInstrumentClass(symbol).ToString();
        }

        private bool IsSymbol(string canonical)
        {
            return SymbolRouting.NormalizeSymbol(_bot.SymbolName) == canonical;
        }

        private static string ResolveSetupType(string entryType)
        {
            if (string.IsNullOrWhiteSpace(entryType))
                return string.Empty;

            var setup = entryType.Trim();

            if (setup.IndexOf("MicroContinuation", StringComparison.OrdinalIgnoreCase) >= 0)
                return "MicroContinuation";
            if (setup.IndexOf("Flag", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Flag";
            if (setup.IndexOf("Pullback", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Pullback";
            if (setup.IndexOf("Breakout", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Breakout";
            if (setup.IndexOf("Transition", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Transition";

            return setup;
        }

        private static HtfBiasSnapshot BuildHtfSnapshotFromContext(EntryContext ctx, InstrumentClass instrumentClass)
        {
            var snapshot = new HtfBiasSnapshot();
            if (ctx == null)
                return snapshot;

            snapshot.AllowedDirection = ctx.ActiveHtfDirection;
            snapshot.Confidence01 = ctx.ActiveHtfConfidence;

            if (instrumentClass == InstrumentClass.FX)
                snapshot.Reason = ctx.FxHtfReason ?? string.Empty;
            else if (instrumentClass == InstrumentClass.CRYPTO)
                snapshot.Reason = ctx.CryptoHtfReason ?? string.Empty;
            else if (instrumentClass == InstrumentClass.METAL)
                snapshot.Reason = ctx.MetalHtfReason ?? string.Empty;
            else if (instrumentClass == InstrumentClass.INDEX)
                snapshot.Reason = ctx.IndexHtfReason ?? string.Empty;

            return snapshot;
        }

        private void ApplyHtfBiasScoreOnly(List<EntryEvaluation> symbolSignals, HtfBiasSnapshot bias, string assetTag)
        {
            if (symbolSignals == null || bias == null)
                return;

            GlobalLogger.Log(_bot, $"[HTF][BIAS] asset={assetTag} direction={bias.AllowedDirection} state={bias.State} impact=ScoreOnly conf={bias.Confidence01:0.00}");

            int alignedCandidates = 0;
            int misalignedCandidates = 0;

            foreach (var candidate in symbolSignals)
            {
                if (candidate == null || candidate.Direction == TradeDirection.None || EntryDecisionPolicy.IsHardInvalid(candidate))
                    continue;

                bool hasDirectionalBias = bias.AllowedDirection != TradeDirection.None;
                bool aligned = hasDirectionalBias && candidate.Direction == bias.AllowedDirection;
                bool misaligned = hasDirectionalBias && candidate.Direction != bias.AllowedDirection;

                if (aligned)
                    alignedCandidates++;

                if (misaligned)
                    misalignedCandidates++;

                int originalScore = candidate.Score;

                if (aligned)
                    candidate.Score += 5;

                if (misaligned)
                {
                    int htfPenalty = 10;
                    bool fxHtfAlreadyPunished =
                        string.Equals(assetTag, "FX", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(candidate.Reason) &&
                        candidate.Reason.IndexOf("HTF", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (fxHtfAlreadyPunished)
                    {
                        htfPenalty = 4;
                        GlobalLogger.Log(_bot,
                            $"[FX][HTF_DOUBLE_GUARD] symbol={candidate.Symbol ?? _bot.SymbolName} entryType={candidate.Type} scorePenalty={htfPenalty} reason=existing_htf_penalty_detected");
                    }

                    candidate.Score -= htfPenalty;
                }

                candidate.Score = Math.Max(0, Math.Min(100, candidate.Score));

                GlobalLogger.Log(_bot, 
                    $"[HTF][CANDIDATE] asset={assetTag} type={candidate.Type} dir={candidate.Direction} " +
                    $"aligned={aligned} misaligned={misaligned} score={originalScore}->{candidate.Score} state={bias.State}");
            }

            GlobalLogger.Log(_bot, 
                $"[HTF][APPLIED] asset={assetTag} dir={bias.AllowedDirection} state={bias.State} " +
                $"alignedBonus=5 misalignedPenalty=10 " +
                $"alignedCandidates={alignedCandidates} misaligned={misalignedCandidates}");
        }

        private static string ResolveMarketRegime(EntryContext entryCtx)
        {
            if (entryCtx == null)
                return string.Empty;

            if (entryCtx.TransitionValid)
                return "Transition";

            var marketState = entryCtx.MarketState;
            if (marketState != null)
            {
                if (marketState.IsTrend)
                    return "Trend";
                if (marketState.IsRange)
                    return "Range";
                if (marketState.IsLowVol)
                    return "LowVol";
            }

            if (entryCtx.IsRange_M5)
                return "Range";

            if (entryCtx.TrendDirection != TradeDirection.None)
                return "Trend";

            return entryCtx.IsAtrExpanding_M5 ? "HighVol" : "LowVol";
        }

        private IndexMarketState BuildEntryMarketState(
            FxMarketState fxState,
            CryptoMarketState cryptoState,
            XauMarketState xauState,
            IndexMarketState indexState)
        {
            if (indexState != null)
                return indexState;

            if (fxState != null)
            {
                return new IndexMarketState
                {
                    IsTrend = fxState.IsTrend,
                    IsMomentum = fxState.IsMomentum,
                    IsLowVol = fxState.IsLowVol,
                    IsRange = fxState.IsCompression,
                    Adx = fxState.Adx,
                    AtrPoints = fxState.AtrPips
                };
            }

            if (cryptoState != null)
            {
                return new IndexMarketState
                {
                    IsTrend = cryptoState.IsTrend,
                    IsMomentum = cryptoState.IsMomentum,
                    IsLowVol = cryptoState.IsLowVol,
                    IsRange = !cryptoState.IsTrend && !cryptoState.IsExpansion,
                    Adx = cryptoState.Adx,
                    AtrPoints = cryptoState.AtrPips
                };
            }

            if (xauState != null)
            {
                return new IndexMarketState
                {
                    IsTrend = xauState.IsTrend,
                    IsMomentum = xauState.IsMomentum,
                    IsLowVol = xauState.IsLowVol,
                    IsRange = xauState.IsRange,
                    Adx = xauState.Adx,
                    AtrPoints = xauState.AtrPips,
                    RangePoints = xauState.RangeWidth
                };
            }

            return null;
        }

        private static double ComputeRMultiple(Position pos, PositionContext? ctx, double pipSize)
        {
            if (pos == null || ctx == null || ctx.RiskPriceDistance <= 0)
                return 0.0;

            return (pos.Pips * pipSize) / ctx.RiskPriceDistance;
        }

        private void LogScalingOpenAudit(Position pos, PositionContext? ctx)
        {
            if (pos == null || ctx == null || ctx.RiskPriceDistance <= 0)
                return;

            var symbol = _runtimeSymbols.ResolveSymbol(pos.SymbolName);
            if (symbol == null || symbol.TickSize <= 0)
                return;

            double balance = _entryBalanceByPositionId.TryGetValue(pos.Id, out var entryBalance)
                ? entryBalance
                : _bot.Account.Balance;
            double accountSize = ResolveAccountSizeAnchor(balance);
            double valuePerPricePerUnit = symbol.TickValue / symbol.TickSize;
            double expectedLossUsd = ctx.RiskPriceDistance * valuePerPricePerUnit * pos.VolumeInUnits;
            double riskPercent = balance > 0 ? (expectedLossUsd / balance) * 100.0 : 0.0;
            double slPips = symbol.PipSize > 0 ? ctx.RiskPriceDistance / symbol.PipSize : 0.0;
            double pipValue = symbol.PipSize * valuePerPricePerUnit;
            double lotSize = symbol.LotSize > 0 ? pos.VolumeInUnits / symbol.LotSize : 0.0;
            double notionalExposure = pos.VolumeInUnits * pos.EntryPrice;
            bool capped = ctx.LotCap > 0 && lotSize + 1e-9 >= ctx.LotCap;

            GlobalLogger.Log(_bot, $"[SCALING][RISK] accountSize={accountSize:0} balance={balance:0.##} riskPercent={riskPercent:0.####} riskUSD={expectedLossUsd:0.####} volume={pos.VolumeInUnits:0.####} slPips={slPips:0.####} pipValue={pipValue:0.####} expectedLossUSD={expectedLossUsd:0.####}");
            GlobalLogger.Log(_bot, $"[SCALING][POSITION] symbol={pos.SymbolName} accountSize={accountSize:0} volume={pos.VolumeInUnits:0.####} lotSize={lotSize:0.####} notionalExposure={notionalExposure:0.####} riskPercent={riskPercent:0.####}");
            GlobalLogger.Log(_bot, $"[SCALING][TP_SL] accountSize={accountSize:0} SL_R=1 TP1_R={ctx.Tp1R:0.####} TP2_R={ctx.Tp2R:0.####} TP1_ratio={ctx.Tp1Ratio:0.####} TP2_ratio={ctx.Tp2Ratio:0.####}");
            GlobalLogger.Log(_bot, $"[SCALING][LOTCAP] accountSize={accountSize:0} desiredVolume=NA actualVolume={pos.VolumeInUnits:0.####} capped={capped.ToString().ToLowerInvariant()} riskDeviationPercent=NA");
        }

        private void LogScalingCloseAudit(Position pos, PositionContext? ctx, double entryBalance)
        {
            if (pos == null || ctx == null || ctx.RiskPriceDistance <= 0)
                return;

            var symbol = _runtimeSymbols.ResolveSymbol(pos.SymbolName);
            if (symbol == null || symbol.PipSize <= 0)
                return;

            double accountSize = ResolveAccountSizeAnchor(entryBalance);
            double rMultiple = ComputeRMultiple(pos, ctx, symbol.PipSize);
            double profitPercent = entryBalance > 0 ? (pos.NetProfit / entryBalance) * 100.0 : 0.0;
            GlobalLogger.Log(_bot, $"[SCALING][RESULT] accountSize={accountSize:0} Rmultiple={rMultiple:0.####} profitUSD={pos.NetProfit:0.####} profitPercent={profitPercent:0.####} MFE_R={ctx.MfeR:0.####} MAE_R={ctx.MaeR:0.####}");

            if (Math.Abs(ctx.Tp1R) < 1e-9 && Math.Abs(ctx.Tp2R) < 1e-9)
            {
                GlobalLogger.Log(_bot, $"[SCALING][ANOMALY] type=tp_sl_missing accountSize={accountSize:0} details=tp_structure_not_resolved");
            }
        }

        private static double ResolveAccountSizeAnchor(double balance)
        {
            if (balance <= 0)
                return 0;

            double[] anchors = { 10000, 25000, 50000, 100000 };
            double nearest = anchors[0];
            double nearestDistance = Math.Abs(balance - nearest);

            for (int i = 1; i < anchors.Length; i++)
            {
                double distance = Math.Abs(balance - anchors[i]);
                if (distance < nearestDistance)
                {
                    nearest = anchors[i];
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        // =========================================================
        // HARD LOSS GUARD – INSTRUMENT AWARE (LEAN VERSION)
        // =========================================================

        private readonly HashSet<long> _hardLossClosing = new();

        private bool CheckHardLoss()
        {
            foreach (var pos in _bot.Positions)
            {
                if (pos == null)
                    continue;

                // Only our bot positions
                if (pos.Label != BotLabel)
                    continue;

                // Only this chart symbol
                if (!pos.SymbolName.Equals(_bot.SymbolName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Prevent duplicate close spam
                if (_hardLossClosing.Contains(pos.Id))
                    continue;

                double loss = pos.NetProfit;

                if (!_positionContexts.TryGetValue(pos.Id, out var ctx))
                    continue;

                // A hard-loss guard monetary limitjét ugyanazzal a dimenzióval számoljuk,
                // mint a risk sizing: price-distance × value-per-price × volume(units).
                // A korábbi pips × pipValue × units képlet instrumentenként félreskálázódott,
                // ezért gyakorlatilag 0-hoz közeli limitet adott és azonnali zárást okozott.
                double valuePerPricePerUnit =
                    _bot.Symbol.TickSize > 0
                        ? (_bot.Symbol.TickValue / _bot.Symbol.TickSize)
                        : 0.0;

                double volumeUnits = pos.VolumeInUnits > 0
                    ? pos.VolumeInUnits
                    : ctx.EntryVolumeInUnits;

                double slRisk = ctx.RiskPriceDistance * valuePerPricePerUnit * volumeUnits;
                if (slRisk <= 0 || double.IsNaN(slRisk) || double.IsInfinity(slRisk))
                    continue;

                double hardLimit = -(slRisk * 1.5);


                if (loss > hardLimit)
                    continue;

                _hardLossClosing.Add(pos.Id);

                GlobalLogger.Log(_bot, 
                    $"[HARD LOSS EXIT] pos={pos.Id} symbol={pos.SymbolName} " +
                    $"net={loss:F2} gross={pos.GrossProfit:F2} " +
                    $"limit={hardLimit:F2}"
                );

                _bot.ClosePosition(pos);
                return true;
            }

            return false;
        }
    }
}
