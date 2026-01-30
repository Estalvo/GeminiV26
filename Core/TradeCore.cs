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
using GeminiV26.Core.Entry;
using GeminiV26.EntryTypes;
using GeminiV26.EntryTypes.FX;
using GeminiV26.EntryTypes.INDEX;
using GeminiV26.EntryTypes.METAL;
using GeminiV26.EntryTypes.Crypto;
using GeminiV26.Interfaces;
using GeminiV26.Data;
using GeminiV26.Data.Models;
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
using GeminiV26.Core;
using GeminiV26.Core.HtfBias;
using System.Linq;

namespace GeminiV26.Core
{
    public class TradeCore
    {
        private readonly Robot _bot;
        private readonly TradeRouter _router;

        private readonly EntryRouter _entryRouter;
        private readonly EntryContextBuilder _contextBuilder;
        private readonly List<IEntryType> _entryTypes;

        private readonly TradeLogger _tradeLogger;
        private readonly Dictionary<long, PositionContext> _positionContexts = new();
        private readonly TradeMetaStore _tradeMetaStore = new();
       
        private const string BotLabel = "GeminiV26";

        // =========================
        // HARD LOSS GUARD (CORE)
        // =========================        
        private const double HARD_MAX_LOSS_MIN_USD = -40.0;   // abszolút védelem
        private const double HARD_MAX_LOSS_PER_LOT = -35.0;   // USD / 1.0 lot

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

        private EntryContext _ctx;

        public TradeCore(Robot bot)
        {
            _bot = bot;
            _router = new TradeRouter(_bot);
            var symbol = NormalizeSymbol(_bot.SymbolName);

            // =========================
            // INSTRUMENT ROUTING (SSOT)
            // =========================

            if (
                symbol.Contains("EURUSD") ||
                symbol.Contains("USDJPY") ||
                symbol.Contains("GBPUSD") ||
                symbol.Contains("AUDUSD") ||
                symbol.Contains("AUDNZD") ||
                symbol.Contains("EURJPY") ||
                symbol.Contains("GBPJPY") ||
                symbol.Contains("NZDUSD") ||
                symbol.Contains("USDCAD") ||
                symbol.Contains("USDCHF")
            )
            {
                _fxMarketStateDetector = new FxMarketStateDetector(_bot, symbol);
                _fxBias = new FxHtfBiasEngine(_bot);
            }
            else if (
                symbol.Contains("BTC") ||
                symbol.Contains("ETH")
            )
            {
                _cryptoMarketStateDetector = new CryptoMarketStateDetector(_bot);
                _cryptoBias = new CryptoHtfBiasEngine(_bot);
            }
            else if (
                symbol.Contains("XAU")
            )
            {
                _xauMarketStateDetector = new XauMarketStateDetector(_bot);
                _metalBias = new MetalHtfBiasEngine(_bot);
            }
            else if (
                symbol.Contains("NAS") ||
                symbol.Contains("US TECH") ||
                symbol.Contains("US 30") ||
                symbol.Contains("GER") ||
                symbol.Contains("DAX")
            )
            {
                _indexMarketStateDetector = new IndexMarketStateDetector(_bot);
                _indexBias = new IndexHtfBiasEngine(_bot);
            }
            else
            {
                _bot.Print($"❌ UNKNOWN SYMBOL ROUTING: {symbol}");
            }


            if (
               symbol.Contains("BTC") ||
               symbol.Contains("ETH")
            )
            {
                _entryTypes = new List<IEntryType>
                {
                    new BTC_PullbackEntry(),
                    new BTC_FlagEntry(),
                    new BTC_RangeBreakoutEntry()
                };
            }

            else if (symbol.Contains("XAU"))
            {
                _entryTypes = new List<IEntryType>
                {                    
                    new XAU_FlagEntry(),
                    new XAU_PullbackEntry(),
                    new XAU_ReversalEntry(),
                    new XAU_ImpulseEntry()
                };
            }
            else if (
                symbol.Contains("EURUSD") ||
                symbol.Contains("USDJPY") ||
                symbol.Contains("GBPUSD") ||
                symbol.Contains("AUDUSD") ||
                symbol.Contains("AUDNZD") ||
                symbol.Contains("EURJPY") ||
                symbol.Contains("GBPJPY") ||
                symbol.Contains("NZDUSD") ||
                symbol.Contains("USDCAD") ||
                symbol.Contains("USDCHF") 
            )
            {
                _entryTypes = new List<IEntryType>
                {
                    new FX_FlagEntry(),                   // ÚJ, valódi flag
                    new FX_ImpulseContinuationEntry(),    // RÉGI "flag"
                    new FX_PullbackEntry(),
                    new FX_RangeBreakoutEntry(),
                    new FX_ReversalEntry()
                };
            }
            else if (
                symbol.Contains("NAS") ||
                symbol.Contains("USTECH") ||     // ← EZ HIÁNYZOTT
                symbol.Contains("US TECH") ||
                symbol.Contains("US 30") ||
                symbol.Contains("US30") ||
                symbol.Contains("GER") ||
                symbol.Contains("DAX"))
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
            _contextBuilder = new EntryContextBuilder(bot);
            _tradeLogger = new TradeLogger(_bot.SymbolName);
            _globalSessionGate = new GlobalSessionGate(_bot);

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
            };

            _bot.Positions.Closed += OnPositionClosed;
        }

        public void OnBar()
        {
            _bot.Print($"[ONBAR DBG] sym={_bot.SymbolName} ctxs={_positionContexts != null}");

            // =========================
            // XAU MarketState (only if XAU wired)
            // =========================
            XauMarketState xauState = null;
            if (_bot.SymbolName.Contains("XAU") && _xauMarketStateDetector != null)
            {
                xauState = _xauMarketStateDetector.Evaluate();
                if (xauState != null)
                {
                    _bot.Print(
                        $"[XAU MarketState] {_bot.SymbolName} " +
                        $"Range={xauState.IsRange} " +
                        $"SoftRange={xauState.IsSoftRange} " +
                        $"Breakout={xauState.IsBreakout} " +
                        $"PostBO={xauState.IsPostBreakout} " +
                        $"ADX={xauState.Adx:F1} " +
                        $"ATR={xauState.Atr:F2} " +
                        $"Width={xauState.RangeWidth:F2}"
                    );
                }
            }

            // =========================
            // Exit management (OnBar)
            // =========================
            foreach (var pos in _bot.Positions)
            {
                if (pos.SymbolName != _bot.SymbolName)
                    continue;

                if (_bot.SymbolName.Contains("XAU"))
                    _xauExitManager?.OnBar(pos);

                else if (IsNasSymbol(_bot.SymbolName))
                    _nasExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("US30"))
                    _us30ExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("EURUSD"))
                    _eurUsdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("USDJPY"))
                    _usdJpyExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("GBPUSD"))
                    _gbpUsdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("AUDUSD"))
                    _audUsdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("AUDNZD"))
                    _audNzdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("EURJPY"))
                    _eurJpyExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("GBPJPY"))
                    _gbpJpyExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("NZDUSD"))
                    _nzdUsdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("USDCAD"))
                    _usdCadExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("USDCHF"))
                    _usdChfExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("BTC"))
                    _btcUsdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("ETH"))
                    _ethUsdExitManager?.OnBar(pos);

                else if (_bot.SymbolName.Contains("GER40") || _bot.SymbolName.Contains("GER"))
                    _ger40ExitManager?.OnBar(pos);
            }

            if (HasOpenGeminiPosition())
            {
                _bot.Print("[DEBUG] HasOpenGeminiPosition = TRUE");
                return;
            }

            _ctx = _contextBuilder.Build(_bot.SymbolName);

            // =========================
            // GLOBAL SESSION GATE
            // =========================
            if (!_globalSessionGate.AllowEntry(_bot.SymbolName))
            {
                _bot.Print("[TC] BLOCKED: Global SessionGate");
                return;
            }

            // =========================
            // FX SESSION INJECT
            // =========================
            if (_fxMarketStateDetector != null)
            {
                if (_bot.SymbolName.Contains("EURUSD"))
                    _ctx.Session = ((EurUsdSessionGate)_eurUsdSessionGate).GetSession();

                else if (_bot.SymbolName.Contains("USDJPY"))
                    _ctx.Session = ((UsdJpySessionGate)_usdJpySessionGate).GetSession();

                else if (_bot.SymbolName.Contains("GBPUSD"))
                    _ctx.Session = ((GbpUsdSessionGate)_gbpUsdSessionGate).GetSession();

                else if (_bot.SymbolName.Contains("AUDUSD"))
                    _ctx.Session = ((AudUsdSessionGate)_audUsdSessionGate).GetSession();

                else if (_bot.SymbolName.Contains("AUDNZD"))
                    _ctx.Session = ((AudNzdSessionGate)_audNzdSessionGate).GetSession();

                else if (_bot.SymbolName.Contains("EURJPY"))
                    _ctx.Session = ((EurJpySessionGate)_eurJpySessionGate).GetSession();

                else if (_bot.SymbolName.Contains("GBPJPY"))
                    _ctx.Session = ((GbpJpySessionGate)_gbpJpySessionGate).GetSession();

                else if (_bot.SymbolName.Contains("NZDUSD"))
                    _ctx.Session = ((NzdUsdSessionGate)_nzdUsdSessionGate).GetSession();

                else if (_bot.SymbolName.Contains("USDCAD"))
                    _ctx.Session = ((UsdCadSessionGate)_usdCadSessionGate).GetSession();

                else if (_bot.SymbolName.Contains("USDCHF"))
                    _ctx.Session = ((UsdChfSessionGate)_usdChfSessionGate).GetSession();
            }

            TradeType xauBias = TradeType.Buy;   // default
            int xauBiasConfidence = 0;

            if (_bot.SymbolName.Contains("XAU"))
                _xauEntryLogic?.Evaluate(out xauBias, out xauBiasConfidence);

            if (_bot.SymbolName.Contains("EURUSD"))
                _eurUsdEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("GBPUSD"))
                _gbpUsdEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("USDJPY"))
                _usdJpyEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("AUDUSD"))
                _audUsdEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("AUDNZD"))
                _audNzdEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("EURJPY"))
                _eurJpyEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("GBPJPY"))
                _gbpJpyEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("NZDUSD"))
                _nzdUsdEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("USDCAD"))
                _usdCadEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("USDCHF"))
                _usdChfEntryLogic?.Evaluate();

            if (IsNasSymbol(_bot.SymbolName))
                _nasEntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("GER40") || _bot.SymbolName.Contains("GER"))
                _ger40EntryLogic?.Evaluate();

            if (_bot.SymbolName.Contains("BTC"))
                _btcUsdEntryLogic?.Evaluate(out _, out _);

            if (_bot.SymbolName.Contains("ETH"))
                _ethUsdEntryLogic?.Evaluate(out _, out _);

            _bot.Print($"[DEBUG] HasOpenGeminiPosition={HasOpenGeminiPosition()}");
            _bot.Print($"[DEBUG] M5.Count={_ctx?.M5?.Count}");

            int minBars = _bot.SymbolName.Contains("EURUSD") ? 10 : 30;
            if (_ctx?.M5 == null || _ctx.M5.Count < minBars) return;

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

            // =========================
            // FX HTF BIAS GATE (Group-level)
            // =========================
            bool isFxSymbol =
                _fxMarketStateDetector != null &&
                !_bot.SymbolName.Contains("XAU") &&
                !IsNasSymbol(_bot.SymbolName) &&
                !_bot.SymbolName.Contains("US30") &&
                !_bot.SymbolName.Contains("GER40") &&
                !_bot.SymbolName.Contains("GER") &&
                !_bot.SymbolName.Contains("BTC") &&
                !_bot.SymbolName.Contains("ETH");

            if (isFxSymbol && _fxBias != null)
            {
                var bias = _fxBias.Get(_bot.SymbolName);

                _ctx.FxHtfAllowedDirection = bias.AllowedDirection;
                _ctx.FxHtfConfidence01 = bias.Confidence01;
                _ctx.FxHtfReason = bias.Reason;

                _bot.Print(
                    $"[FX HTF] allow={bias.AllowedDirection} " +
                    $"conf={bias.Confidence01:0.00} reason={bias.Reason}"
                );

                // ❌ nincs return
                // ❌ nincs direction filter
            }

            else if (isCryptoSymbol && _cryptoBias != null)
            {
                var bias = _cryptoBias.Get(_bot.SymbolName);

                _bot.Print($"[CRYPTO HTF] state={bias.State} allow={bias.AllowedDirection}");

                if (bias.State == HtfBiasState.Neutral ||
                    bias.State == HtfBiasState.Transition)
                    return;

                symbolSignals = symbolSignals
                    .Where(e => e == null || !e.IsValid || e.Direction == bias.AllowedDirection)
                    .ToList();

                if (symbolSignals.All(e => e == null || !e.IsValid))
                {
                    _bot.Print("[TC] CRYPTO HTF BLOCK: all candidates filtered by bias");
                    return;
                }
            }

            else if (isMetalSymbol && _metalBias != null)
            {
                var bias = _metalBias.Get(_bot.SymbolName);

                _bot.Print($"[XAU HTF] state={bias.State} allow={bias.AllowedDirection}");

                if (bias.State == HtfBiasState.Neutral ||
                    bias.State == HtfBiasState.Transition)
                    return;

                symbolSignals = symbolSignals
                    .Where(e => e == null || !e.IsValid || e.Direction == bias.AllowedDirection)
                    .ToList();

                if (symbolSignals.All(e => e == null || !e.IsValid))
                {
                    _bot.Print("[TC] XAU HTF BLOCK: all candidates filtered by bias");
                    return;
                }
            }

            else if (isIndexSymbol && _indexBias != null)
            {
                var bias = _indexBias.Get(_bot.SymbolName);

                _bot.Print($"[INDEX HTF] state={bias.State} allow={bias.AllowedDirection}");

                if (bias.State == HtfBiasState.Neutral ||
                    bias.State == HtfBiasState.Transition)
                    return;

                symbolSignals = symbolSignals
                    .Where(e => e == null || !e.IsValid || e.Direction == bias.AllowedDirection)
                    .ToList();

                if (symbolSignals.All(e => e == null || !e.IsValid))
                {
                    _bot.Print("[TC] INDEX HTF BLOCK: all candidates filtered by bias");
                    return;
                }
            }


            var selected = _router.SelectEntry(symbolSignals);

            // =========================
            // TRACE – was there a selected entry?
            // =========================
            _bot.Print($"[TRACE] selected is null = {selected == null}");

            if (selected == null)
            {
                _bot.Print("[TC] NO SELECTED ENTRY (all invalid)");
                return;
            }

            _tradeMetaStore.RegisterPending(
                _bot.SymbolName,
                new PendingEntryMeta
                {
                    EntryType = selected.Type.ToString(),
                    EntryReason = selected.Reason,
                    Confidence = Convert.ToInt32(selected.Score)
                }
            );

            _bot.Print($"[TC] ENTRY WINNER {selected.Type} dir={selected.Direction} score={selected.Score}");

            // =========================
            // XAU DIR INJECT (ONLY IF ENTRY DID NOT DECIDE)
            // =========================
            if (_bot.SymbolName.Contains("XAU") && selected.Direction == TradeDirection.None)
            {
                selected.Direction =
                    xauBias == TradeType.Buy
                        ? TradeDirection.Long
                        : TradeDirection.Short;

                _bot.Print($"[TC][XAU] DIR INJECTED (fallback) {selected.Direction} (conf={xauBiasConfidence})");
            }

            if (_bot.SymbolName.Contains("XAU"))
            {
                _bot.Print(
                    $"[ASSERT][XAU] after inject: " +
                    $"selected.Type={selected.Type} " +
                    $"dir={selected.Direction} " +
                    $"score={selected.Score} " +
                    $"valid={selected.IsValid}"
                );
            }

            // ✅ SAFETY: diag fallback / invalid eval ne crasheljen
            if (selected.Direction == TradeDirection.None)
            {
                _bot.Print($"[TC] ENTRY DROPPED: Direction=None (type={selected.Type} score={selected.Score} reason={selected.Reason})");
                return;
            }

            var gateDir = ToTradeTypeStrict(selected.Direction);

            // =========================
            // XAU range hard block (only if state exists)
            // =========================
            if (_bot.SymbolName.Contains("XAU") && xauState != null)
            {
                bool hardRange = xauState.IsRange;
                if (hardRange)
                {
                    _bot.Print(
                        $"[TC] ENTRY BLOCKED: XAU HARD RANGE " +
                        $"Width={xauState.RangeWidth:F2} ADX={xauState.Adx:F1} Vol={xauState.VolumeNorm:F2}"
                    );
                    return;
                }
            }

            // === GATES ONLY ===
            if (_bot.SymbolName.Contains("XAU"))
            {
                if (!(_xauSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: XAU SessionGate");
                    return;
                }

                if (!(_xauImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: XAU ImpulseGate");
                    return;
                }

                _xauExecutor?.ExecuteEntry(selected);
            }
            else if (IsNasSymbol(_bot.SymbolName))
            {
                if (!(_nasSessionGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: NAS SessionGate");
                    return;
                }

                if (!(_nasImpulseGate?.AllowEntry(gateDir) ?? false))
                {
                    _bot.Print("[TC] BLOCKED: NAS ImpulseGate");
                    return;
                }

                _nasExecutor?.ExecuteEntry(selected);
            }
            else if (IsUs30(_bot.SymbolName))
            {
                if (!_us30SessionGate.AllowEntry(gateDir)) return;
                if (!_us30ImpulseGate.AllowEntry(gateDir)) return;
                _us30Executor.ExecuteEntry(selected);
            }
            else if (_bot.SymbolName.Contains("GER40"))
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

                _ger40Executor?.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("EURUSD"))
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

                _eurUsdExecutor?.ExecuteEntry(selected);
              
            }
            else if (_bot.SymbolName.Contains("USDJPY"))
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

                _usdJpyExecutor?.ExecuteEntry(selected);
            }
            else if (_bot.SymbolName.Contains("GBPUSD"))
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

                _gbpUsdExecutor?.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("AUDUSD"))
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

                _audUsdExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("AUDNZD"))
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

                _audNzdExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("EURJPY"))
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

                _eurJpyExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("GBPJPY"))
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

                _gbpJpyExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("NZDUSD"))
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

                _nzdUsdExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("USDCAD"))
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

                _usdCadExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("USDCHF"))
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

                _usdChfExecutor.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("BTC"))
            {
                // BTC: direction mismatch safety
                TradeType routerTradeType =
                    selected.Direction == TradeDirection.Long ? TradeType.Buy : TradeType.Sell;

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
                _btcUsdExecutor?.ExecuteEntry(selected);
            }

            else if (_bot.SymbolName.Contains("ETH"))
            {
                // ETH: direction mismatch safety
                TradeType routerTradeType =
                    selected.Direction == TradeDirection.Long ? TradeType.Buy : TradeType.Sell;

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
                _ethUsdExecutor?.ExecuteEntry(selected);
            }
            else if (IsGer40(_bot.SymbolName))
            {
                if (!_ger40SessionGate.AllowEntry(gateDir)) return;
                if (!_ger40ImpulseGate.AllowEntry(gateDir)) return;
                _ger40Executor.ExecuteEntry(selected);
            }
        }
        
        // =========================================================
        // TICK-LEVEL EXIT DISPATCH (isolated & safe)
        // =========================================================
        public void OnTick()
        {
            try
            {
                // =====================================================
                // HARD LOSS GUARD – GLOBAL SAFETY
                // =====================================================
                if (CheckHardLoss())
                    return;

                if (_bot.SymbolName.Contains("XAU"))
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
                else if (_bot.SymbolName.Contains("US30"))
                {
                    try { _us30ExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][US30] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("GER40") || _bot.SymbolName.Contains("GER"))
                {
                    try { _ger40ExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][GER40] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("EURUSD"))
                {
                    try { _eurUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][EURUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("USDJPY"))
                {
                    try { _usdJpyExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][USDJPY] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("GBPUSD"))
                {
                    try { _gbpUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][GBPUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("AUDUSD"))
                {
                    try { _audUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][AUDUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("AUDNZD"))
                {
                    try { _audNzdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][AUDNZD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("EURJPY"))
                {
                    try { _eurJpyExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][EURJPY] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("GBPJPY"))
                {
                    try { _gbpJpyExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][GBPJPY] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("NZDUSD"))
                {
                    try { _nzdUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][NZDUSD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("USDCAD"))
                {
                    try { _usdCadExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][USDCAD] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("USDCHF"))
                {
                    try { _usdChfExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][USDCHF] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("BTC"))
                {
                    try { _btcUsdExitManager?.OnTick(); }
                    catch (Exception ex)
                    {
                        _bot.Print($"[TC][ONTICK][BTC] {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else if (_bot.SymbolName.Contains("ETH"))
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

            var sym = _bot.Symbols.GetSymbol(pos.SymbolName);

            _tradeLogger.Log(new TradeRecord
            {
                CloseTimestamp = _bot.Server.Time,

                Symbol = pos.SymbolName,
                PositionId = pos.Id,
                Direction = pos.TradeType.ToString(),

                EntryType = meta?.EntryType,
                EntryReason = meta?.EntryReason,
                Confidence = meta?.Confidence,

                // --- Exit diagnostics ---
                Tp1Hit = ctx != null ? (bool?)ctx.Tp1Hit : null,
                Tp2Hit = ctx != null ? (bool?)(ctx.Tp2Hit > 0.0) : null,

                EntryPrice = pos.EntryPrice,
                ExitPrice = pos.EntryPrice
                    + pos.Pips * sym.PipSize * (pos.TradeType == TradeType.Buy ? 1 : -1),

                VolumeInUnits = ctx?.InitialVolumeInUnits > 0
                    ? ctx.InitialVolumeInUnits
                    : pos.VolumeInUnits,

                NetProfit = pos.NetProfit,
                GrossProfit = pos.GrossProfit,
                Commissions = pos.Commissions,
                Swap = pos.Swap,

                ExitReason = args.Reason.ToString(),
                ExitMode = exitMode,

                EntryTime = pos.EntryTime,
                ExitTime = _bot.Server.Time,
                Pips = pos.Pips,

                EntryVolumeInUnits = ctx?.InitialVolumeInUnits,
                Tp1ClosedVolumeInUnits = ctx?.Tp1ClosedVolumeInUnits,
                RemainingVolumeInUnits = ctx?.RemainingVolumeInUnits,

                BeActivated = ctx?.BeActivated,
                TrailingActivated = ctx?.TrailingActivated,
            });

            _positionContexts.Remove(pos.Id);
            _tradeMetaStore.Remove(pos.Id);
        }

        public void OnStop()
        {
            _bot.Positions.Closed -= OnPositionClosed;
        }

        // =================================================
        // SYMBOL NORMALIZATION
        // =================================================
        private static string NormalizeSymbol(string symbol)
        {
            return symbol
                .ToUpperInvariant()
                .Replace(" ", "")
                .Replace("_", "");
        }

        // =================================================
        // INDEX HELPERS
        // =================================================
        private static bool IsNasSymbol(string symbol)
        {
            var s = NormalizeSymbol(symbol);
            return s.Contains("NAS")
                || s.Contains("US100")
                || s.Contains("USTECH100");
        }

        private static bool IsUs30(string symbol)
        {
            var s = NormalizeSymbol(symbol);
            return s == "US30";
        }

        private static bool IsGer40(string symbol)
        {
            var s = NormalizeSymbol(symbol);
            return s == "GER40"
                || s == "GERMANY40"
                || s == "DE40";
        }

        private static bool IsIndexSymbol(string symbol)
        {
            return IsNasSymbol(symbol)
                || IsUs30(symbol)
                || IsGer40(symbol);
        }

        // =========================================================
        // HARD LOSS GUARD – CORE LEVEL (CTX-INDEPENDENT)
        // =========================================================
        private readonly HashSet<long> _hardLossClosing = new();

        private bool CheckHardLoss()
        {
            foreach (var pos in _bot.Positions)
            {
                if (pos == null)
                    continue;

                // only our bot's positions
                if (pos.Label != BotLabel)
                    continue;

                // only this chart's symbol
                if (!pos.SymbolName.Equals(_bot.SymbolName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // avoid spamming close calls on the same position
                if (_hardLossClosing.Contains(pos.Id))
                    continue;

                // Use NET profit for safety (includes commissions/swap impact)
                double loss = pos.NetProfit;

                // -------------------------------
                // Dynamic hard loss calculation
                // -------------------------------

                // base protection up to 0.5 lot
                const double BASE_HARD_LOSS_USD = -40.0;

                // additional loss allowed per extra lot above 0.5
                const double LOSS_PER_EXTRA_LOT = -20.0;

                // absolute safety cap (never allow more loss than this)
                const double ABSOLUTE_HARD_LOSS_CAP = -80.0;

                // convert volume to lots (symbol-aware)
                double volumeLots = pos.VolumeInUnits / _bot.Symbol.VolumeInUnitsMin;

                // calculate extra lots above 0.5
                double extraLots = Math.Max(0.0, volumeLots - 0.5);

                // linear scaling
                double dynamicHardLoss = BASE_HARD_LOSS_USD + LOSS_PER_EXTRA_LOT * extraLots;

                // apply absolute cap
                dynamicHardLoss = Math.Max(dynamicHardLoss, ABSOLUTE_HARD_LOSS_CAP);

                // only active losing positions beyond hard limit
                if (loss > dynamicHardLoss)
                    continue;

                _hardLossClosing.Add(pos.Id);

                _bot.Print(
                    $"[HARD LOSS EXIT] pos={pos.Id} symbol={pos.SymbolName} " +
                    $"net={loss:F2} gross={pos.GrossProfit:F2} " +
                    $"limit={dynamicHardLoss:F2} lot={volumeLots:F2}"
                );

                _bot.ClosePosition(pos);
                return true; // stop further exit processing this tick
            }

            return false;
        }

        
        /*
        // LEGACY (unused since Phase 3.x)
        // Kept for reference only – NOT CALLED

        private List<IEntryType> BuildEntryTypesForSymbol(string symbol)
        {
            if (symbol == null)
                return new List<IEntryType>();

            // =========================
            // METAL
            // =========================
            if (symbol.Contains("XAU"))
            {
                return new List<IEntryType>
                {
                    new XAU_PullbackEntry(),   // ⭐ EZ A KULCS
                    new XAU_ImpulseEntry(),
                    new XAU_FlagEntry(),
                    new XAU_ReversalEntry()
                };
            }

            // =========================
            // INDEX (EGY ÁG!)
            // =========================
            if (IsIndexSymbol(symbol))
            {
                return new List<IEntryType>
                {
                    new Index_BreakoutEntry()
                };
            }

            // =========================
            // CRYPTO
            // =========================
            if (
                symbol.Contains("BTC") ||
                symbol.Contains("ETH")
            )
            {
                return new List<IEntryType>
                {
                    new Crypto_ImpulseEntry(),
                    new BTC_FlagEntry(),
                    new BTC_PullbackEntry(),
                    new BTC_RangeBreakoutEntry()
                };
            }


            // =========================
            // FX (DEFAULT)
            // =========================
            return new List<IEntryType>
            {
                new FX_FlagEntry(),
                new FX_ImpulseContinuationEntry(),
                new FX_PullbackEntry(),
                new FX_RangeBreakoutEntry(),
                new FX_ReversalEntry()
            };
        }*/
    }
}
