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

        private bool isFxSymbol;
        private bool isCryptoSymbol;
        private bool isMetalSymbol;
        private bool isIndexSymbol;

        private GlobalSessionGate _globalSessionGate;
        private SessionMatrix _sessionMatrix;

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
                _bot.Print($"❌ UNKNOWN SYMBOL ROUTING: {symbol}");
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
                _bot.Print($"[WARN] Unknown symbol fallback used: {symbol}");

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
            Action<string> safePrint = msg => _bot.BeginInvokeOnMainThread(() => _bot.Print(msg));
            _flagBreakoutDetector = new FlagBreakoutDetector(safePrint);
            _logWriter = new LogWriter(safePrint);
            _logger = new CompositeTradeLogger(
                new CsvTradeLogger(_logWriter, safePrint),
                new CsvAnalyticsLogger(_logWriter, safePrint));
            _statsTracker = new TradeStatsTracker(safePrint);
            _memoryEngine = new MarketMemoryEngine(safePrint);
            _contextBuilder = new EntryContextBuilder(bot, _memoryEngine);
            _globalSessionGate = new GlobalSessionGate(_bot);
            _sessionMatrix = new SessionMatrix(new SessionMatrixProvider());

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

                _bot.Print(bound
                    ? $"[META BIND OK] pos={pos.Id} symbol={pos.SymbolName}"
                    : $"[META BIND FAIL] pos={pos.Id} symbol={pos.SymbolName} (NO PENDING)"
                );

                if (_ctx != null)
                {
                    _contextRegistry.RegisterEntry(pos.Id, _ctx);
                    _statsTracker.RegisterTradeOpen(_ctx, pos.Id);
                }

                if (_positionContexts.TryGetValue(pos.Id, out var pctx))
                {
                    pctx.FinalDirection = _ctx?.FinalDirection != TradeDirection.None
                        ? _ctx.FinalDirection
                        : FromTradeType(pos.TradeType);
                    _bot.Print($"[DIR][SET] posId={pctx.PositionId} finalDir={pctx.FinalDirection}");

                    if (pctx.FinalDirection == TradeDirection.None)
                    {
                        _bot.Print($"[DIR][POS_CTX_ERROR] Missing FinalDirection posId={pctx.PositionId} sym={pctx.Symbol}");
                        return;
                    }

                    if (_ctx != null && pctx.FinalDirection != _ctx.FinalDirection)
                    {
                        _bot.Print($"[DIR][FATAL_MISMATCH] sym={_bot.SymbolName} stage=position posId={pctx.PositionId} posFinal={pctx.FinalDirection} entryFinal={_ctx.FinalDirection}");
                        return;
                    }

                    _contextRegistry.RegisterPosition(pctx);
                    _bot.Print($"[DIR][POS_CTX] posId={pctx.PositionId} sym={pctx.Symbol} finalDir={pctx.FinalDirection}");
                }

                _tradeMetaStore.TryGet(pos.Id, out var pendingMeta);
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
            _bot.Print("[RESOLVER][INIT] mode=runtime_only phase=OnStart");
        }

        public void OnBar()
        {
            EnsureRuntimeResolverInitialized();
            _runtimeSymbols.BeginExecutionCycle();
            string rawSym = _bot.SymbolName;
            string sym = NormalizeSymbol(rawSym);   // ✅ CANONICAL

            _bot.Print($"[ONBAR DBG] raw={rawSym} canonical={sym}");

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
                    _bot.Print($"[FX MarketState] {rawSym} Trend={fxState.IsTrend} Momentum={fxState.IsMomentum} LowVol={fxState.IsLowVol} ADX={fxState.Adx:F1}");
            }
            else if (isCrypto)
            {
                cryptoState = _cryptoMarketStateDetector.Evaluate();
                if (cryptoState != null)
                    _bot.Print($"[CRYPTO MarketState] {rawSym} Trend={cryptoState.IsTrend} Momentum={cryptoState.IsMomentum} LowVol={cryptoState.IsLowVol} ADX={cryptoState.Adx:F1}");
            }
            else if (isMetal)
            {
                xauState = _xauMarketStateDetector.Evaluate();
                if (xauState != null)
                    _bot.Print($"[XAU MarketState] {rawSym} Range={xauState.IsRange} Trend={xauState.IsTrend} Momentum={xauState.IsMomentum} ADX={xauState.Adx:F1} HardRange={xauState.IsHardRange}");
            }
            else if (isIndex)
            {
                indexState = _indexMarketStateDetector.Evaluate();
                if (indexState != null)
                    _bot.Print($"[INDEX MarketState] {rawSym} Trend={indexState.IsTrend} Momentum={indexState.IsMomentum} LowVol={indexState.IsLowVol} ADX={indexState.Adx:F1}");
            }
            else
            {
                _bot.Print($"[TC] WARN: Unknown instrument type in OnBar sym={rawSym}");
            }

            // =========================
            // ADDED: prevent NRE if contexts dictionary isn't initialized yet
            // =========================
            if (_positionContexts == null)
            {
                _bot.Print("BLOCK: _positionContexts == null");
                _bot.Print("[TC] WARN: _positionContexts is NULL (skip exit+entry pipeline this bar)");
                return;
            }

            // =========================
            // Exit management (OnBar)
            // =========================
            foreach (var pos in _bot.Positions)
            {
                if (pos.SymbolName != _bot.SymbolName)
                    continue;

                _bot.Print($"[EXIT DBG] posId={pos.Id} sym={pos.SymbolName}");

                // ⛔ TEMP SAFETY (you already had this)
                if (!_positionContexts.TryGetValue(pos.Id, out var ctx))
                {
                    _bot.Print($"[TC] Context missing for position posId={pos.Id}");
                    _bot.Print($"[REHYDRATE_WARN] pos={Convert.ToInt64(pos.Id)} symbol={pos.SymbolName} reason=exit_pipeline_missing_context");
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
                    _bot.Print($"[TC] Pruned stale context: positionId={id}"));
                _lastContextPruneUtc = _bot.Server.Time;
            }

            if (HasOpenGeminiPosition())
            {
                _bot.Print("[DEBUG] HasOpenGeminiPosition = TRUE");
                _bot.Print("BLOCK: existing Gemini position open");
                return;
            }

            // =========================
            // ADDED: hard null guards before building context / routing
            // =========================
            if (_contextBuilder == null)
            {
                _bot.Print("BLOCK: _contextBuilder == null");
                _bot.Print("[TC] ERROR: _contextBuilder is NULL (cannot build entry context)");
                return;
            }
            if (_globalSessionGate == null)
            {
                _bot.Print("BLOCK: _globalSessionGate == null");
                _bot.Print("[TC] ERROR: _globalSessionGate is NULL (cannot gate entries)");
                return;
            }
            if (_entryRouter == null)
            {
                _bot.Print("BLOCK: _entryRouter == null");
                _bot.Print("[TC] ERROR: _entryRouter is NULL (cannot evaluate entries)");
                return;
            }

            _ctx = _contextBuilder.Build(_bot.SymbolName);

            // ADDED: context must be ready
            if (_ctx != null)
                _ctx.Log = _bot.Print;
            
            if (_ctx == null || !_ctx.IsReady)
            {
                _bot.Print("BLOCK: EntryContext not ready");
                _bot.Print("[TC] BLOCKED: EntryContext not ready");
                return;
            }

            _ctx.BarsSinceStart = BotRestartState.BarsSinceStart;


            if (isMetalSymbol)
            {
                if (xauState == null && _xauMarketStateDetector != null)
                    xauState = _xauMarketStateDetector.Evaluate();
            }

            _ctx.MarketState = BuildEntryMarketState(fxState, cryptoState, xauState, indexState);
            if (_ctx.MarketState != null)
            {
                _bot.Print(
                    "[ENTRY MARKETSTATE ASSIGN] sym={0} trend={1} momentum={2} lowVol={3} adx={4:F1} atrPts={5:F2}",
                    rawSym,
                    _ctx.MarketState.IsTrend,
                    _ctx.MarketState.IsMomentum,
                    _ctx.MarketState.IsLowVol,
                    _ctx.MarketState.Adx,
                    _ctx.MarketState.AtrPoints);
            }

            _ctx.LastUpdateUtc = DateTime.UtcNow;
            _contextRegistry.RegisterEntry(_ctx);
            _contextRegistry.RebuildFromActivePositions(_bot.Positions, _positionContexts);

            var transition = _transitionDetector.Evaluate(_ctx);
            _ctx.Transition = transition;
            _ctx.TransitionValid = transition.IsValid;
            _ctx.TransitionScoreBonus = transition.BonusScore;
            _flagBreakoutDetector.Evaluate(_ctx);
            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][CTX_BUILD] sym={_bot.SymbolName} trend={_ctx.TrendDirection} impulse={_ctx.ImpulseDirection} breakout={_ctx.BreakoutDirection} reversal={_ctx.ReversalDirection}", _ctx));

            // =========================
            // GLOBAL SESSION GATE + SESSION MATRIX
            // =========================
            SessionDecision sessionDecision = _globalSessionGate.GetDecision(_bot.SymbolName, _bot.TimeFrame);
            _bot.Print($"[SESSION CHECK] time={_bot.Server.Time:O} symbol={_bot.SymbolName} bucket={sessionDecision.Bucket} allow={sessionDecision.Allow}");
            if (!sessionDecision.Allow)
            {
                _bot.Print("BLOCK: session gate");
                _bot.Print("[TC] BLOCKED: Global SessionGate");
                return;
            }

            string instrumentClass = ResolveInstrumentClass(symU);
            SessionMatrixConfig sessionCfg = _sessionMatrix.Resolve(sessionDecision, instrumentClass, _bot.TimeFrame);
            _ctx.SessionMatrixConfig = sessionCfg;

            _bot.Print("[SESSION_MATRIX] symbol={0} bucket={1} tier={2} flag={3} breakout={4} pullback={5} minADX={6:F1} minAtrMult={7:F2}",
                _bot.SymbolName,
                sessionDecision.Bucket,
                SessionMatrix.DetectTier(_bot.TimeFrame),
                sessionCfg.AllowFlag,
                sessionCfg.AllowBreakout,
                sessionCfg.AllowPullback,
                sessionCfg.MinAdx,
                sessionCfg.MinAtrMultiplier);

            // =========================
            // SESSION INJECT (STRICT FROM GLOBAL GATE BUCKET)
            // =========================
            _ctx.Session = SessionResolver.FromBucket(sessionDecision.Bucket);
            _bot.Print("[CTX_SESSION_ASSIGN] sessionFromGate={0} sessionAssigned={1}", sessionDecision.Bucket, _ctx.Session);
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
                _ctx.Print("[BIAS FALLBACK] using TrendDirection");
            }

            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][LOGIC] sym={_bot.SymbolName} logicBias={_ctx.LogicBiasDirection} logicConf={_ctx.LogicBiasConfidence}", _ctx));
            _bot.Print(TradeLogIdentity.WithTempId($"[CTX PROPAGATION] symbol={_bot.SymbolName} bias={_ctx.LogicBias} conf={_ctx.LogicConfidence}", _ctx));

            if (IsSymbol("AUDNZD"))
                _ctx.Print($"[AUDNZD TRACE] step2_ctx={_ctx.LogicBiasDirection} conf={_ctx.LogicBiasConfidence}");

            _bot.Print($"[DEBUG] HasOpenGeminiPosition={HasOpenGeminiPosition()}");
            _bot.Print($"[DEBUG] M5.Count={_ctx?.M5?.Count}");

            int minBars = IsSymbol("EURUSD") ? 10 : 30;
            if (_ctx?.M5 == null || _ctx.M5.Count < minBars) return;

            _entryRouterPassCounter++;
            _ctx.DirectionDebugLogged = false;
            _bot.Print(TradeLogIdentity.WithTempId($"[PIPE][ENTRY_ROUTER_PASS] pass={_entryRouterPassCounter} symbol={_bot.SymbolName} bar={_bot.Server.Time:O}", _ctx));
            _bot.Print(TradeLogIdentity.WithTempId($"[ENTRY START] symbol={_bot.SymbolName} bias={_ctx.LogicBias}", _ctx));

            var signals = _entryRouter.Evaluate(new[] { _ctx });

            _bot.Print(
                $"[PIPE] symbol={_bot.SymbolName} " +
                $"hasSignals={signals.ContainsKey(_bot.SymbolName)} " +
                $"count={(signals.ContainsKey(_bot.SymbolName) ? signals[_bot.SymbolName].Count : -1)}"
            );

            _bot.Print($"[DEBUG] signals.Keys = {string.Join(",", signals.Keys)}");

            if (!signals.TryGetValue(_bot.SymbolName, out var symbolSignals))
            {
                _bot.Print("[DEBUG] NO signals for symbol");
                return;
            }

            ApplyTransitionScoreBoost(_ctx, symbolSignals);

            _bot.Print(TradeLogIdentity.WithTempId($"[DBG ENTRY] total candidates={symbolSignals.Count}", _ctx));

            foreach (var e in symbolSignals)
            {
                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][ROUTER_CAND] sym={_bot.SymbolName} type={e?.Type} valid={e?.IsValid} score={e?.Score} dir={e?.Direction} reason={e?.Reason}", _ctx));
            }

        // =====================================================
        // HTF BIAS = SCORE-ONLY CONTEXT (Asset-group level)
        // Handles FX / Crypto / Metals / Index policies without filtering
        // =====================================================

        if (isFxSymbol && _fxBias != null)
        {
            var bias = _fxBias.Get(_bot.SymbolName);

            _ctx.FxHtfAllowedDirection = bias.AllowedDirection;
            _ctx.FxHtfConfidence01 = bias.Confidence01;
            _ctx.FxHtfReason = bias.Reason;

            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "FX");
        }
        else if (isCryptoSymbol && _cryptoBias != null)
        {
            var bias = _cryptoBias.Get(_bot.SymbolName);
            _ctx.CryptoHtfAllowedDirection = bias.AllowedDirection;
            _ctx.CryptoHtfConfidence01 = bias.Confidence01;
            _ctx.CryptoHtfReason = bias.Reason;

            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "CRYPTO");
        }
        else if (isMetalSymbol && _metalBias != null)
        {
            var bias = _metalBias.Get(_bot.SymbolName);
            _ctx.MetalHtfAllowedDirection = bias.AllowedDirection;
            _ctx.MetalHtfConfidence01 = bias.Confidence01;
            _ctx.MetalHtfReason = bias.Reason;

            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "XAU");
        }
        else if (isIndexSymbol && _indexBias != null)
        {
            var bias = _indexBias.Get(_bot.SymbolName);
            _ctx.IndexHtfAllowedDirection = bias.AllowedDirection;
            _ctx.IndexHtfConfidence01 = bias.Confidence01;
            _ctx.IndexHtfReason = bias.Reason;

            _bot.Print(TradeLogIdentity.WithTempId($"[DIR][HTF] sym={_bot.SymbolName} allow={bias.AllowedDirection} conf={bias.Confidence01:0.00} reason={bias.Reason}", _ctx));
            ApplyHtfBiasScoreOnly(symbolSignals, bias, "INDEX");
        }

                UpdateExecutionStateMachine(_ctx, symbolSignals);
                ApplyRestartProtection(_ctx, symbolSignals);

                // =====================================================
                // ROUTER
                // =====================================================
                var selected = _router.SelectEntry(symbolSignals, _ctx);

                _bot.Print($"[TRACE] selected is null = {selected == null}");

                if (selected == null)
                {
                    _bot.Print("BLOCK: entry gate");
                    _bot.Print("[TC] NO SELECTED ENTRY (all invalid)");
                    return;
                }


                // =====================================================
                // XAU HARD RANGE BLOCK
                // =====================================================
                if (IsSymbol("XAUUSD") &&
                    _xauMarketStateDetector != null)
                {
                    xauState = _xauMarketStateDetector.Evaluate();

                    if (xauState != null && xauState.IsRange && !xauState.IsTrend)
                    {
                        _bot.Print(
                            $"[TC] ENTRY BLOCKED: XAU RANGE REGIME" +
                            $"Width={xauState.RangeWidth:F2} " +
                            $"ADX={xauState.Adx:F1} " +
                            $"ATR={xauState.Atr:F2}"
                        );
                        return;
                    }
                }


                // =====================================================
                // META STORE GUARD
                // =====================================================
                if (_tradeMetaStore == null)
                {
                    _bot.Print("[TC] ERROR: _tradeMetaStore is NULL (skip entry)");
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
                        Confidence = Convert.ToInt32(selected.Score)
                    }
                );

                LogEntrySnapshot(_ctx, selected);
                _bot.Print(TradeLogIdentity.WithTempId($"[TC] ENTRY WINNER {selected.Type} dir={selected.Direction} score={selected.Score}", _ctx));
                _bot.Print($"[POS ?] [ENTRY] symbol={selected.Symbol ?? _bot.SymbolName} score={selected.Score} direction={selected.Direction}");
                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][ROUTED] sym={_bot.SymbolName} type={selected.Type} routedDir={selected.Direction} score={selected.Score}", _ctx));

                if (!PassFinalAcceptance(_ctx, selected))
                {
                    _bot.Print("BLOCK: final acceptance gate");
                    return;
                }

                _ctx.RoutedDirection = selected.Direction;
                _ctx.FinalDirection = selected.Direction;
                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][SET] sym={_ctx.Symbol} finalDir={_ctx.FinalDirection}", _ctx));

                if (_ctx.FinalDirection == TradeDirection.None)
                {
                    _bot.Print("BLOCK: direction/entry failed");
                    _bot.Print($"[TC] ENTRY DROPPED: Direction=None (type={selected.Type} score={selected.Score} reason={selected.Reason})");
                    return;
                }

                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][FINAL] sym={_bot.SymbolName} routed={_ctx.RoutedDirection} final={_ctx.FinalDirection}", _ctx));
                DirectionGuard.Validate(_ctx, null, _bot.Print);

                if (!ValidateDirectionConsistency(_ctx, selected))
                {
                    _bot.Print("BLOCK: direction/entry failed");
                    return;
                }

                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][EXEC_PRE] sym={_bot.SymbolName} finalCtxDir={_ctx.FinalDirection}", _ctx));
                _bot.Print(TradeLogIdentity.WithTempId($"[DIR][EXEC_CONFIRMED] sym={_bot.SymbolName} finalDir={_ctx.FinalDirection}", _ctx));

                if (!HasDirectionTraceCompleteness(_ctx))
                    _bot.Print(TradeLogIdentity.WithTempId($"[DIR][TRACE_INCOMPLETE] sym={_bot.SymbolName} finalDir={_ctx.FinalDirection}", _ctx));

                var gateDir = ToTradeTypeStrict(_ctx.FinalDirection);
                _bot.Print("CHECK: direction gate");
                _bot.Print("CHECK: entry gate");

            // === GATES ONLY ===
            if (IsSymbol("XAUUSD"))
            {
                if (!(_xauSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("BLOCK: session gate");
                    _bot.Print("[TC] BLOCKED: XAU SessionGate");
                    return;
                }

                if (!(_xauImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("BLOCK: direction/entry failed");
                    _bot.Print("[TC] BLOCKED: XAU ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _xauExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsNasSymbol(_bot.SymbolName))
            {
                if (!(_nasSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("BLOCK: session gate");
                    _bot.Print("[TC] BLOCKED: NAS SessionGate");
                    return;
                }

                if (!(_nasImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("BLOCK: direction/entry failed");
                    _bot.Print("[TC] BLOCKED: NAS ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _nasExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsUs30(_bot.SymbolName))
            {
                if (!_us30SessionGate.AllowEntry(gateDir)) return;
                if (!_us30ImpulseGate.AllowEntry(gateDir)) return;
                LogEntryExecuted(selected);
                _us30Executor.ExecuteEntry(selected, _ctx);
            }
            else if (IsSymbol("GER40"))
            {
                if (!(_ger40SessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: GER40 SessionGate");
                    return;
                }

                if (!(_ger40ImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: GER40 ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _ger40Executor?.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("EURUSD"))
            {
                if (_eurUsdSessionGate != null && !_eurUsdSessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: EUR SessionGate");
                    return;
                }

                if (_eurUsdImpulseGate != null && !_eurUsdImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: EUR ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _eurUsdExecutor?.ExecuteEntry(selected, _ctx);
              
            }
            else if (IsSymbol("USDJPY"))
            {
                if (!(_usdJpySessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: USDJPY SessionGate");
                    return;
                }

                if (!(_usdJpyImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: USDJPY ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _usdJpyExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsSymbol("GBPUSD"))
            {
                if (!(_gbpUsdSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: GBPUSD SessionGate");
                    return;
                }

                if (!(_gbpUsdImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: GBPUSD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _gbpUsdExecutor?.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("AUDUSD"))
            {
                if (!_audUsdSessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: AUDUSD SessionGate");
                    return;
                }
                
                if (!_audUsdImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: AUDUSD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _audUsdExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("AUDNZD"))
            {
                if (!_audNzdSessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: AUDNZD SessionGate");
                    return;
                }

                if (!_audNzdImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: AUDNZD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _audNzdExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("EURJPY"))
            {
                if (!_eurJpySessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: EURJPY SessionGate");
                    return;
                }

                if (!_eurJpyImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: EURJPY ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _eurJpyExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("GBPJPY"))
            {
                if (!_gbpJpySessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: GBPJPY SessionGate");
                    return;
                }

                if (!_gbpJpyImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: GBPJPY ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _gbpJpyExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("NZDUSD"))
            {
                if (!_nzdUsdSessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: NZDUSD SessionGate");
                    return;
                }

                if (!_nzdUsdImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: NZDUSD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _nzdUsdExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("USDCAD"))
            {
                if (!_usdCadSessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: USDCAD SessionGate");
                    return;
                }

                if (!_usdCadImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: USDCAD ImpulseGate");
                    return;
                }

                LogEntryExecuted(selected);
                _usdCadExecutor.ExecuteEntry(selected, _ctx);
            }

            else if (IsSymbol("USDCHF"))
            {
                if (!_usdChfSessionGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: USDCHF SessionGate");
                    return;
                }

                if (!_usdChfImpulseGate.AllowEntry(gateDir))
                {
                    _bot.Print("[TC] BLOCKED: USDCHF ImpulseGate");
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
                    _bot.Print($"[TC] ENTRY BLOCKED: Direction mismatch router={routerTradeType} gate={gateDir}");
                    return;
                }

                if (!(_btcUsdSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: BTC SessionGate");
                    return;
                }

                if (!(_btcUsdImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: BTC ImpulseGate");
                    return;
                }

                _bot.Print("[BTC GATE] ALLOWED (Session+Impulse)");
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
                    _bot.Print($"[TC] ENTRY BLOCKED: Direction mismatch router={routerTradeType} gate={gateDir}");
                    return;
                }

                if (!(_ethUsdSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: ETH SessionGate");
                    return;
                }

                if (!(_ethUsdImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: ETH ImpulseGate");
                    return;
                }

                _bot.Print("[ETH GATE] ALLOWED (Session+Impulse)");
                LogEntryExecuted(selected);
                _ethUsdExecutor?.ExecuteEntry(selected, _ctx);
            }
            else if (IsGer40(_bot.SymbolName))
            {
                if (!_ger40SessionGate.AllowEntry(gateDir)) return;
                if (!_ger40ImpulseGate.AllowEntry(gateDir)) return;
                LogEntryExecuted(selected);
                _ger40Executor.ExecuteEntry(selected, _ctx);
            }
        }

        private bool ValidateDirectionConsistency(EntryContext entryContext, EntryEvaluation entry)
        {
            if (entryContext == null || entry == null)
            {
                _bot.Print($"[DIR][FATAL_MISMATCH] sym={_bot.SymbolName} reason=null_context_or_entry");
                _bot.Print("[TC] ENTRY BLOCKED: direction consistency check failed");
                return false;
            }

            if (entry.Direction != entryContext.FinalDirection)
            {
                _bot.Print($"[DIR][EXEC_MISMATCH] sym={_bot.SymbolName} entryDir={entry.Direction} finalDir={entryContext.FinalDirection}");
            }

            return true;
        }

        private static bool HasDirectionTraceCompleteness(EntryContext ctx)
        {
            return ctx != null
                && ctx.RoutedDirection != TradeDirection.None
                && ctx.FinalDirection != TradeDirection.None;
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
                _bot.Print($"[ENTRY][STRUCTURE] score boost applied type={entry.Type} boost={boost} score={entry.Score} transition={ctx.TransitionValid} breakout={ctx.FlagBreakoutConfirmed}");
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

                if (!TryGetRestartDecayState(ctx, candidate, out string restartReason))
                    continue;

                if (BotRestartState.IsHardProtectionPhase)
                {
                    bool isCryptoCandidate = IsCryptoCandidate(candidate.Type);
                    bool continuationAuthority = HasContinuationAuthority(ctx, candidate);
                    bool midTrend =
                        ctx.TrendDirection == candidate.Direction &&
                        ctx.MarketState?.IsTrend == true;
                    _bot.Print(TradeLogIdentity.WithTempId(
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
                            candidate.IsValid = true;
                            EntryDecisionPolicy.Normalize(candidate);

                            _bot.Print(TradeLogIdentity.WithTempId(
                                $"[ENTRY][PROTECT_SUPPRESSED] source=RESTART_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                                $"type={candidate.Type} dir={candidate.Direction} score={protectedOriginalScore}->{candidate.Score} state={restartReason}",
                                ctx));
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
                            candidate.IsValid = true;
                            EntryDecisionPolicy.Normalize(candidate);

                            _bot.Print(TradeLogIdentity.WithTempId(
                                $"[ENTRY][MID_TREND] active=true restartPenalty={restartPenalty} earlyBreakPenalty=0 symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} dir={candidate.Direction}",
                                ctx));
                            _bot.Print(TradeLogIdentity.WithTempId(
                                $"[ENTRY][PROTECT] source=RESTART_PROTECT action=MID_TREND_SOFT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                                $"type={candidate.Type} dir={candidate.Direction} score={midTrendOriginalScore}->{candidate.Score} state={restartReason}",
                                ctx));
                            continue;
                        }

                        candidate.IsValid = false;
                        candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                            ? "[RESTART_DECAY_AFTER_RESTART]"
                            : $"{candidate.Reason} [RESTART_DECAY_AFTER_RESTART]";
                        EntryDecisionPolicy.Normalize(candidate);

                        _bot.Print(TradeLogIdentity.WithTempId(
                            $"[ENTRY][PROTECT] source=RESTART_PROTECT action=HARD_BLOCK symbol={candidate.Symbol ?? _bot.SymbolName} " +
                            $"type={candidate.Type} dir={candidate.Direction} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                            ctx));
                        _bot.Print(TradeLogIdentity.WithTempId(
                            $"[RESTART BLOCK] reason=DECAY_AFTER_RESTART symbol={candidate.Symbol ?? _bot.SymbolName} " +
                            $"type={candidate.Type} dir={candidate.Direction} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                            ctx));
                        continue;
                    }

                    int hardPhaseCryptoPenalty = 12;
                    int originalScore = candidate.Score;
                    candidate.Score = Math.Max(CryptoSurvivableScoreFloor, candidate.Score - hardPhaseCryptoPenalty);
                    candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                        ? $"[RESTART_SOFT_AFTER_RESTART_{restartReason}]"
                        : $"{candidate.Reason} [RESTART_SOFT_AFTER_RESTART_{restartReason}]";
                    EntryDecisionPolicy.Normalize(candidate);

                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[ENTRY][PROTECT] source=RESTART_PROTECT action=SOFT_PENALTY symbol={candidate.Symbol ?? _bot.SymbolName} " +
                        $"type={candidate.Type} dir={candidate.Direction} score={originalScore}->{candidate.Score} state={restartReason}",
                        ctx));
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[RESTART SOFT-HARDPHASE] symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} " +
                        $"dir={candidate.Direction} score={originalScore}->{candidate.Score} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                        ctx));
                    continue;
                }

                if (!BotRestartState.IsSoftProtectionPhase)
                    continue;

                const int softPenalty = 20;
                int originalSoftScore = candidate.Score;
                candidate.Score = Math.Max(0, candidate.Score - softPenalty);
                candidate.Reason = string.IsNullOrWhiteSpace(candidate.Reason)
                    ? $"[RESTART_SOFT_{restartReason}]"
                    : $"{candidate.Reason} [RESTART_SOFT_{restartReason}]";
                EntryDecisionPolicy.Normalize(candidate);

                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[RESTART SOFT] penalty applied symbol={candidate.Symbol ?? _bot.SymbolName} type={candidate.Type} " +
                    $"dir={candidate.Direction} score={originalSoftScore}->{candidate.Score} barsSinceStart={ctx.BarsSinceStart} state={restartReason}",
                    ctx));
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

            int normalizedEntryScore = PositionContext.ClampRiskConfidence(selected.Score);
            int logicConfidence = Math.Max(0, ctx.LogicBiasConfidence);
            int normalizedLogicConfidence = PositionContext.ClampRiskConfidence(logicConfidence);
            int finalConfidence = PositionContext.ComputeFinalConfidenceValue(normalizedEntryScore, normalizedLogicConfidence);
            int riskFinal = PositionContext.ClampRiskConfidence(finalConfidence);

            _bot.Print(TradeLogIdentity.WithTempId(
                $"[SCORE][PRODUCER] source=EntryScore raw={selected.Score} normalized={normalizedEntryScore}",
                ctx));
            _bot.Print(TradeLogIdentity.WithTempId(
                $"[SCORE][PRODUCER] source=LogicConfidence raw={logicConfidence} normalized={normalizedLogicConfidence}",
                ctx));
            _bot.Print(TradeLogIdentity.WithTempId(
                $"[SCORE][BLEND] entry={normalizedEntryScore} logic={normalizedLogicConfidence} final={finalConfidence}",
                ctx));

            _bot.Print(TradeLogIdentity.WithTempId(
                TradeAuditLog.BuildEntrySnapshot(_bot, ctx, selected, normalizedLogicConfidence, finalConfidence, 0, riskFinal),
                ctx));
            _bot.Print(TradeLogIdentity.WithTempId(
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

            _bot.Print($"[MEMORY][RECOVER] symbol={normalized} source={source}");
            _memoryEngine.BuildFromHistory(normalized, LoadMemoryHistory(normalized));

            SymbolMemoryState rebuiltState = _memoryEngine.GetState(normalized);
            if (rebuiltState == null || !rebuiltState.IsBuilt)
            {
                _bot.Print($"[MEMORY][CRITICAL_MISSING] symbol={normalized} source={source}");
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
                    _bot.Print($"[STARTUP][TRACE] before_resolve symbol={symbol}");

                var runtimeSymbol = ResolveSymbol(symbol);

                if (DebugStartupTrace)
                    _bot.Print($"[STARTUP][TRACE] after_resolve symbol={symbol} resolved={(runtimeSymbol != null)}");

                if (!IsTradable(runtimeSymbol))
                {
                    _bot.Print("BLOCK: not tradable");
                    _bot.Print($"[MEMORY][SKIP] {symbol}");
                    continue;
                }

                if (DebugStartupTrace)
                    _bot.Print($"[STARTUP][TRACE] initialize_memory symbol={symbol}");

                _memoryEngine.Initialize(symbol);
                _memoryEngine.BuildFromHistory(symbol, LoadMemoryHistory(symbol));
            }

            _isMemoryReady = true;
            EmitStartupCoverageLogs(symbols);
            _bot.Print($"[BOOT][MEMORY_READY] symbols={symbols.Count}");
        }

        private List<Bar> LoadMemoryHistory(string symbol)
        {
            if (string.IsNullOrWhiteSpace(symbol))
                return new List<Bar>();

            if (!_runtimeSymbols.TryGetBars(TimeFrame.Minute5, symbol, out Bars bars))
            {
                string normalizedSymbol = NormalizeSymbol(symbol);
                _memoryEngine.MarkResolveFailure(normalizedSymbol, "unresolved_runtime_symbol");
                _bot.Print($"[MEMORY][SYMBOL_UNRESOLVED] canonical={normalizedSymbol}");
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
                _bot.Print(TradeLogIdentity.WithTempId(
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

            _bot.Print(TradeLogIdentity.WithTempId(
                $"[ENTRY][AUTH] source=EARLY_BREAK_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                $"type={candidate.Type} dir={candidate.Direction} authority={continuationAuthority}",
                ctx));

            if (continuationAuthority)
            {
                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[ENTRY][PROTECT_SUPPRESSED] source=EARLY_BREAK_PROTECT symbol={candidate.Symbol ?? _bot.SymbolName} " +
                    $"type={candidate.Type} dir={candidate.Direction} score={originalScore}->{candidate.Score} penalty={appliedPenalty} barsSinceBreak={barsSinceBreak}",
                    ctx));
                return;
            }

            _bot.Print(TradeLogIdentity.WithTempId(
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
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[ENTRY][REJECT][EARLY_NO_STRUCTURE] {candidate.Symbol ?? _bot.SymbolName} {candidate.Type} {candidate.Direction} phase={movePhase} source={(phaseFromCtx ? "CTX" : "MEM")} barsSinceBreak={barsSinceBreak} pullback={ctx.BarsSinceFirstPullback} score={candidate.Score}",
                        ctx));
                    continue;
                }

                if (!trigger.IsManaged)
                {
                    candidate.TriggerConfirmed = true;
                    candidate.State = EntryState.TRIGGERED;
                    continue;
                }

                candidate.TriggerConfirmed = true;
                candidate.State = EntryState.TRIGGERED;

                if (!trigger.TriggerConfirmed)
                {
                    UpsertArmedSetup(candidate, barsSinceBreak);
                    _bot.Print($"[SETUP DETECTED] symbol={candidate.Symbol} score={candidate.Score} state=ARMED type={candidate.Type} dir={candidate.Direction}");
                    _bot.Print($"[TRIGGER WAIT] symbol={candidate.Symbol} reason={trigger.WaitReason} type={candidate.Type} dir={candidate.Direction} impact=score_only");
                }
                else
                {
                    UpsertArmedSetup(candidate, barsSinceBreak);
                    _bot.Print($"[TRIGGER CONFIRMED] symbol={candidate.Symbol} breakoutClose={trigger.BreakoutClose.ToString().ToLowerInvariant()} structureBreak={trigger.StructureBreak.ToString().ToLowerInvariant()} m1Break={trigger.M1Break.ToString().ToLowerInvariant()} type={candidate.Type} dir={candidate.Direction}");
                }
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
                    diagnostics.BreakoutClose =
                        close > rangeHigh + buffer ||
                        close < rangeLow - buffer;
                }

                double prevHigh = ctx.M5.HighPrices[lastClosedM5 - 1];
                double prevLow = ctx.M5.LowPrices[lastClosedM5 - 1];
                double currentHigh = ctx.M5.HighPrices[lastClosedM5];
                double currentLow = ctx.M5.LowPrices[lastClosedM5];

                diagnostics.StructureBreak =
                    currentHigh > prevHigh ||
                    currentLow < prevLow ||
                    ctx.BrokeLastSwingHigh_M5 ||
                    ctx.BrokeLastSwingLow_M5;
            }

            diagnostics.M1Break =
                ctx.HasBreakout_M1 &&
                ctx.BreakoutDirection == candidate.Direction;

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

            string symbol = ctx.Symbol ?? _bot.SymbolName;
            bool isLong = eval.Direction == TradeDirection.Long;
            bool isShort = eval.Direction == TradeDirection.Short;

            if ((isLong && ctx.IsOverextendedLong) ||
                (isShort && ctx.IsOverextendedShort))
            {
                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[FINAL][REJECT][OVEREXT] {symbol} {eval.Type} {eval.Direction} score={eval.Score} trend={ctx.TrendDirection} conf={ctx.LogicBiasConfidence}",
                    ctx));
                return false;
            }

            bool weakSetup = !eval.HasStrongTrigger && !eval.HasStrongStructure;
            if ((isLong && ctx.HasLateContinuationLong) ||
                (isShort && ctx.HasLateContinuationShort))
            {
                if (weakSetup)
                {
                    _bot.Print(TradeLogIdentity.WithTempId(
                        $"[FINAL][REJECT][LATE] {symbol} {eval.Type} {eval.Direction} score={eval.Score} trend={ctx.TrendDirection} conf={ctx.LogicBiasConfidence}",
                        ctx));
                    return false;
                }
            }

            if (ctx.MarketState?.IsTrend == true &&
                ctx.LogicBiasConfidence >= 60 &&
                ctx.TrendDirection != TradeDirection.None &&
                eval.Direction != ctx.TrendDirection)
            {
                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[FINAL][REJECT][TREND] {symbol} {eval.Type} {eval.Direction} score={eval.Score} trend={ctx.TrendDirection} conf={ctx.LogicBiasConfidence}",
                    ctx));
                return false;
            }

            if (weakSetup &&
                eval.Score < EntryDecisionPolicy.MinScoreThreshold + 2)
            {
                _bot.Print(TradeLogIdentity.WithTempId(
                    $"[FINAL][REJECT][WEAK] {symbol} {eval.Type} {eval.Direction} score={eval.Score} trend={ctx.TrendDirection} conf={ctx.LogicBiasConfidence}",
                    ctx));
                return false;
            }

            _bot.Print(TradeLogIdentity.WithTempId(
                $"[FINAL][PASS] {symbol} {eval.Type} {eval.Direction} score={eval.Score} trend={ctx.TrendDirection} conf={ctx.LogicBiasConfidence}",
                ctx));
            return true;
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

        private void UpsertArmedSetup(EntryEvaluation candidate, int barsSinceBreak)
        {
            if (candidate == null)
                return;

            string key = GetArmedSetupKey(candidate);
            _armedSetups[key] = new ArmedSetup
            {
                Symbol = candidate.Symbol ?? _bot.SymbolName,
                Direction = candidate.Direction,
                Score = candidate.Score,
                DetectedAt = _bot.Server.Time,
                BarsSince = barsSinceBreak,
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
            _bot.Print($"[ENTRY EXECUTED] symbol={candidate.Symbol ?? _bot.SymbolName} score={candidate.Score} type={candidate.Type} dir={candidate.Direction}");
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
                    _bot.Print("BLOCK: hard loss guard");
                    return;
                }

                if (IsSymbol("XAUUSD"))
                {
                    try { _xauExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][XAU] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsNasSymbol(_bot.SymbolName))
                {
                    try { _nasExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][NAS] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("US30"))
                {
                    try { _us30ExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][US30] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("GER40"))
                {
                    try { _ger40ExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][GER40] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("EURUSD"))
                {
                    try { _eurUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][EURUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("USDJPY"))
                {
                    try { _usdJpyExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][USDJPY] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("GBPUSD"))
                {
                    try { _gbpUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][GBPUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("AUDUSD"))
                {
                    try { _audUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][AUDUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("AUDNZD"))
                {
                    try { _audNzdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][AUDNZD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("EURJPY"))
                {
                    try { _eurJpyExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][EURJPY] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("GBPJPY"))
                {
                    try { _gbpJpyExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][GBPJPY] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("NZDUSD"))
                {
                    try { _nzdUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][NZDUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("USDCAD"))
                {
                    try { _usdCadExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][USDCAD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("USDCHF"))
                {
                    try { _usdChfExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][USDCHF] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("BTCUSD"))
                {
                    try { _btcUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][BTC] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (IsSymbol("ETHUSD"))
                {
                    try { _ethUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][ETH] {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _bot.Print($"[TC][ONTICK][FATAL] {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static TradeType ToTradeTypeStrict(TradeDirection d)
        {
            if (d == TradeDirection.Long) return TradeType.Buy;
            if (d == TradeDirection.Short) return TradeType.Sell;
            throw new ArgumentException("TradeDirection.None is not allowed");
        }

        private TradeDirection ResolveHtfAllowedDirection(EntryContext ctx)
        {
            if (ctx == null)
                return TradeDirection.None;

            var instrumentClass = SymbolRouting.ResolveInstrumentClass(SymbolRouting.NormalizeSymbol(ctx.Symbol));
            if (instrumentClass == InstrumentClass.FX) return ctx.FxHtfAllowedDirection;
            if (instrumentClass == InstrumentClass.CRYPTO) return ctx.CryptoHtfAllowedDirection;
            if (instrumentClass == InstrumentClass.METAL) return ctx.MetalHtfAllowedDirection;
            if (instrumentClass == InstrumentClass.INDEX) return ctx.IndexHtfAllowedDirection;
            return TradeDirection.None;
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

            if (meta == null)
            {
                _bot.Print(
                    $"[META MISSING] pos={pos.Id} symbol={pos.SymbolName}"
                );
            }

            string exitMode = args.Reason.ToString();

            if (ctx != null)
            {
                if (ctx.Tp2Hit > 0)
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
                _bot.Print($"[EXIT][WARN] pos={pos.Id} symbol={pos.SymbolName} reason=missing_runtime_pipsize");
            }

            _bot.Print(TradeLogIdentity.WithPositionIds(
                $"[EXIT][BROKER_CLOSE_DETECTED]\nreason={MapBrokerCloseReason(args.Reason)}",
                ctx,
                pos));
            _bot.Print(TradeLogIdentity.WithPositionIds(
                TradeAuditLog.BuildExitSnapshot(
                    ctx,
                    pos,
                    args.Reason.ToString(),
                    _bot.Server.Time,
                    exitPrice),
                ctx,
                pos));
            _bot.Print(TradeLogIdentity.WithPositionIds($"[EXIT][DECISION]\nreason={args.Reason}\ndetail=broker_closed_event", ctx, pos));

            _logger.OnTradeClosed(
                BuildLogContext(pos, meta, ctx, entryCtx),
                pos,
                new TradeLogResult
                {
                    ExitMode = exitMode,
                    ExitReason = args.Reason.ToString(),
                    ExitTimeUtc = _bot.Server.Time,
                    ExitPrice = exitPrice,
                    NetProfit = pos.NetProfit,
                    GrossProfit = pos.GrossProfit,
                    Commissions = pos.Commissions,
                    Swap = pos.Swap,
                    Pips = pos.Pips
                });

            _statsTracker.RegisterTradeClose(
                pos.Id,
                entryCtx,
                pos.NetProfit,
                new TradeStatsTracker.TradeCloseSnapshot
                {
                    TimestampUtc = _bot.Server.Time.ToUniversalTime(),
                    Symbol = pos.SymbolName,
                    Direction = pos.TradeType.ToString(),
                    EntryPrice = pos.EntryPrice,
                    ExitPrice = pos.EntryPrice + pos.Pips * sym.PipSize * (pos.TradeType == TradeType.Buy ? 1 : -1),
                    Profit = pos.NetProfit,
                    Score = null,
                    Confidence = meta?.Confidence,
                    SetupType = ResolveSetupType(meta?.EntryType),
                    MarketRegime = ResolveMarketRegime(entryCtx),
                    MfeR = ctx?.MfeR ?? 0.0,
                    MaeR = ctx != null ? -Math.Abs(ctx.MaeR) : 0.0,
                    RMultiple = ComputeRMultiple(pos, ctx, sym.PipSize),
                    TransitionQuality = entryCtx?.TransitionValid == true
                        ? entryCtx.Transition?.QualityScore ?? 0.0
                        : 0.0
                });

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
                TransitionQuality = entryCtx?.TransitionValid == true
                    ? entryCtx.Transition?.QualityScore ?? 0.0
                    : 0.0,
                Confidence = ctx?.FinalConfidence ?? meta?.Confidence ?? 0.0,
                EntryTime = ctx?.EntryTime ?? pos.EntryTime,
                ExitTime = _bot.Server.Time
            };

            _tradeMemoryStore.AddRecord(memoryRecord);
            _memoryLogger.LogWrite(memoryRecord);

            _positionContexts.Remove(pos.Id);
            _contextRegistry.RemovePosition(pos.Id);
            _contextRegistry.RemoveEntry(pos.Id);
            _tradeMetaStore.Remove(pos.Id);
            _bot.Print(TradeLogIdentity.WithPositionIds(TradeAuditLog.BuildCleanup(pos.Id, "position_closed_event"), ctx, pos));
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
                _bot.Print("[BOOT][REHYDRATE_BLOCKED] reason=memory_not_ready");
                return;
            }

            _bot.Print("[BOOT][REHYDRATE_START]");
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
            _bot.Print($"[BOOT][REHYDRATE_DONE] restored={restored} skipped={skipped} failed={failed} openPositions={openCount}");
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

                    _bot.Print($"[MEMORY][MISSING] symbol={symbol} built={isBuilt} stateNull={isNull}");

                    if (isStartupWindow)
                        _bot.Print($"[MEMORY][CRITICAL] symbol={symbol} missing_after_startup");

                    continue;
                }

                if (!isResolved)
                {
                    _bot.Print($"[MEMORY][MISSING] symbol={symbol} built={isBuilt} resolved={isResolved} usable={isUsable} reason={memoryState?.ResolveFailureReason ?? string.Empty}");
                    continue;
                }

                if (MarketMemoryEngine.DebugMemory)
                {
                    _bot.Print(
                        $"[DEBUG][MEMORY][OK] symbol={symbol} phase={memoryState.MovePhase} age={memoryState.MoveAgeBars} pullbacks={memoryState.PullbackCount} usable={isUsable}");
                }
            }

            EmitStartupCoverageLogs(symbols);
        }

        private void EmitStartupCoverageLogs(List<string> symbols)
        {
            if (_startupCoverageLogged)
                return;

            _bot.Print($"[MEMORY][COVERAGE] built={_memoryEngine.GetBuiltCoverageRatio(symbols)}");
            _bot.Print($"[MEMORY][RESOLVE_COVERAGE] resolved={_memoryEngine.GetResolvedCoverageRatio(symbols)}");
            _bot.Print($"[MEMORY][USABLE_COVERAGE] usable={_memoryEngine.GetUsableCoverageRatio(symbols)}");
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

                _bot.Print($"[RESOLVER][MISSING] symbol={symbol}");
                if (isStartupWindow)
                    _bot.Print($"[RESOLVER][CRITICAL] symbol={symbol} missing_after_startup");
            }

            _bot.Print($"[RESOLVER][VALIDATION] resolved={resolved}/{total}");
            if (resolved < total)
                _bot.Print($"[RESOLVER][ERROR] validation_failed resolved={resolved}/{total}");

            _bot.Print($"[RESOLVER][COVERAGE] resolved={resolved}/{total}");
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
                _bot.Print($"[REHYDRATE_WARN] pos={ctx.PositionId} symbol={ctx.Symbol} reason=symbol_not_tradable");
                return false;
            }

            var manager = GetExitManager(canonical);
            if (manager != null)
            {
                manager.RegisterContext(ctx);
                return true;
            }

            _bot.Print($"[REHYDRATE_WARN] pos={ctx.PositionId} symbol={ctx.Symbol} reason=no_exit_manager_for_symbol");
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

        private static bool IsIndexSymbol(string symbol)
        {
            return IsNasSymbol(symbol)
                || IsUs30(symbol)
                || IsGer40(symbol);
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

        private void ApplyHtfBiasScoreOnly(List<EntryEvaluation> symbolSignals, HtfBiasSnapshot bias, string assetTag)
        {
            if (symbolSignals == null || bias == null)
                return;

            _bot.Print($"[HTF][BIAS] asset={assetTag} direction={bias.AllowedDirection} state={bias.State} impact=ScoreOnly conf={bias.Confidence01:0.00}");

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
                    candidate.Score -= 10;

                candidate.Score = Math.Max(0, Math.Min(100, candidate.Score));

                _bot.Print(
                    $"[HTF][CANDIDATE] asset={assetTag} type={candidate.Type} dir={candidate.Direction} " +
                    $"aligned={aligned} misaligned={misaligned} score={originalScore}->{candidate.Score} state={bias.State}");
            }

            _bot.Print(
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

                _bot.Print(
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
