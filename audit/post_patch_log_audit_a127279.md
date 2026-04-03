# Post-patch runtime audit snapshot (reference: a127279)

## Scope
- Runtime files scanned: 15
- AUDNZD: 2026-04-03 10:15:02.675 -> 2026-04-03 10:15:02.968
- AUDUSD: 2026-04-03 10:15:02.561 -> 2026-04-03 10:20:11.551
- BTCUSD: 2026-04-03 10:15:00.224 -> 2026-04-03 10:20:00.456
- ETHUSD: 2026-04-03 10:15:07.741 -> 2026-04-03 10:20:01.313
- EURJPY: 2026-04-03 10:15:00.224 -> 2026-04-03 10:20:12.174
- EURUSD: 2026-04-03 10:15:01.666 -> 2026-04-03 10:20:12.170
- GBPJPY: 2026-04-03 10:15:00.555 -> 2026-04-03 10:20:25.304
- GBPUSD: 2026-04-03 10:15:00.457 -> 2026-04-03 10:20:30.218
- GLOBAL: 2026-04-03 10:15:00.633 -> 2026-04-03 10:20:30.208
- NZDUSD: 2026-04-03 10:15:01.492 -> 2026-04-03 10:20:00.201
- US 30: 2026-04-03 10:15:00.224 -> 2026-04-03 10:20:01.643
- US TECH 100: 2026-04-03 10:15:00.311 -> 2026-04-03 10:20:00.160
- USDCAD: 2026-04-03 10:15:04.658 -> 2026-04-03 10:20:00.914
- USDCHF: 2026-04-03 10:15:06.183 -> 2026-04-03 10:20:02.501
- USDJPY: 2026-04-03 10:15:06.266 -> 2026-04-03 10:20:28.019

## AUTH tags
- [STRUCT][AUTH][DIR_OK]: count=0; instruments=[]; first=None; last=None
- [STRUCT][AUTH][IMPULSE_OK]: count=0; instruments=[]; first=None; last=None
- [STRUCT][AUTH][PULLBACK_OK]: count=0; instruments=[]; first=None; last=None
- [STRUCT][AUTH][FLAG_OK]: count=0; instruments=[]; first=None; last=None
- [STRUCT][AUTH][ALT_PATH_USED]: count=0; instruments=[]; first=None; last=None

## Legacy/fail-chain counters
- NO_DIRECTION: count=173; instruments=['AUDNZD', 'AUDUSD', 'BTCUSD', 'ETHUSD', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD', 'GLOBAL', 'NZDUSD', 'US 30', 'US TECH 100', 'USDCAD', 'USDCHF', 'USDJPY']
- NO_VALID_PULLBACK: count=15; instruments=['AUDNZD', 'AUDUSD', 'BTCUSD', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD', 'US 30', 'US TECH 100', 'USDCAD', 'USDCHF']
- FLAG_PREREQ_NO_PULLBACK: count=25; instruments=['AUDNZD', 'AUDUSD', 'BTCUSD', 'ETHUSD', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD', 'NZDUSD', 'US 30', 'US TECH 100', 'USDCAD', 'USDCHF', 'USDJPY']
- NO_STRUCTURE: count=0; instruments=[]
- ENTRY_NO_PULLBACK: count=4; instruments=['BTCUSD', 'GLOBAL']
- ENTRY_NO_IMPULSE: count=0; instruments=[]
- ENTRY_INVALID_FLAG: count=4; instruments=['BTCUSD', 'GLOBAL']
- [STRUCT][CHAIN][FULL_FAIL]: count=27; instruments=['AUDNZD', 'AUDUSD', 'BTCUSD', 'ETHUSD', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD', 'NZDUSD', 'US 30', 'US TECH 100', 'USDCAD', 'USDCHF', 'USDJPY']
- ROUTER_NO_CANDIDATE: count=54; instruments=['AUDNZD', 'AUDUSD', 'BTCUSD', 'ETHUSD', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD', 'NZDUSD', 'US 30', 'US TECH 100', 'USDCAD', 'USDCHF', 'USDJPY']

## Full-fail contradiction scan
- cases with FULL_FAIL and downstream winner/execute/order/position: 0

## Representative sequences
### EURUSD
- L18: [2026-04-03 10:15:01.730] [STRUCT][PULLBACK][FAIL] symbol=EURUSD dir=Long code=PULLBACK_DEPTH_INVALID depth=0.00 bars=0 shallowDepth=false
- L20: [2026-04-03 10:15:01.731] [STRUCT][FLAG][FAIL] symbol=EURUSD dir=Long code=FLAG_PREREQ_NO_PULLBACK pullback=false shallow=false micro=false
- L21: [2026-04-03 10:15:01.731] [STRUCT][CHAIN][FULL_FAIL] dir=Long source=TREND_DIRECTION impulse=true impulseRecent=true impulseAge=4 pb=false pbDepth=0.00 pbBars=0 flag=false flagBars=0 comp=0.00
- L93: [2026-04-03 10:15:02.031] [EURUSD] attemptId=EUR7X0K6P [ATTEMPT EUR7X0K6P] [DECISION][REJECT_FINAL][CODE=ROUTER_NO_CANDIDATE] detail=no_selected_entry
### USDCAD
- L19: [2026-04-03 10:15:04.743] [STRUCT][FLAG][FAIL] symbol=USDCAD dir=None code=FLAG_PREREQ_NO_PULLBACK pullback=false shallow=false micro=false
- L20: [2026-04-03 10:15:04.743] [STRUCT][CHAIN][FULL_FAIL] dir=None source=NONE impulse=false impulseRecent=false impulseAge=0 pb=false pbDepth=0.00 pbBars=0 flag=false flagBars=0 comp=0.00
- L82: [2026-04-03 10:15:05.024] [USDCAD] attemptId=USD7X0MI1 [ATTEMPT USD7X0MI1] [DECISION][REJECT_FINAL][CODE=ROUTER_NO_CANDIDATE] detail=no_selected_entry
### BTCUSD
- L21: [2026-04-03 10:15:00.385] [STRUCT][CHAIN][FULL_FAIL] dir=Short source=DI_DOMINANCE_SHORT impulse=true impulseRecent=true impulseAge=2 pb=false pbDepth=0.00 pbBars=0 flag=false flagBars=0 comp=0.00
- L89: [2026-04-03 10:15:00.708] [BTCUSD] attemptId=BTC7X0J3N [ATTEMPT BTC7X0J3N] [ROUTER][RANK_ONLY][NO_WINNER][CODE=ROUTER_NO_EXECUTABLE_CANDIDATE] detail=no_executable_candidate
- L92: [2026-04-03 10:15:00.712] [BTCUSD] attemptId=BTC7X0J3N [ATTEMPT BTC7X0J3N] [DECISION][REJECT_FINAL][CODE=ROUTER_NO_CANDIDATE] detail=no_selected_entry
### US TECH 100
- L18: [2026-04-03 10:15:00.393] [STRUCT][PULLBACK][FAIL] symbol=US TECH 100 dir=Short code=PULLBACK_DEPTH_INVALID depth=0.00 bars=0 shallowDepth=false
- L21: [2026-04-03 10:15:00.393] [STRUCT][CHAIN][FULL_FAIL] dir=Short source=TREND_DIRECTION impulse=true impulseRecent=false impulseAge=0 pb=false pbDepth=0.00 pbBars=0 flag=false flagBars=0 comp=0.00
- L96: [2026-04-03 10:15:00.726] [US TECH 100] attemptId=US 7X0J4X [ATTEMPT US 7X0J4X] [DECISION][REJECT_FINAL][CODE=ROUTER_NO_CANDIDATE] detail=no_selected_entry

## Instrument-group coverage
- FX: present runtime logs=['AUDNZD', 'AUDUSD', 'EURJPY', 'EURUSD', 'GBPJPY', 'GBPUSD', 'NZDUSD', 'USDCAD', 'USDCHF', 'USDJPY']
- INDEX: present runtime logs=['US 30', 'US TECH 100']
- XAU: present runtime logs=[]
- CRYPTO: present runtime logs=['BTCUSD', 'ETHUSD']


## Upstream-success yet FULL_FAIL examples
- EURUSD second cycle: [STRUCT][DIR][SOURCE] + [STRUCT][IMPULSE][RECENT_OK] + [STRUCT][PULLBACK][STANDARD_OK] then [STRUCT][CHAIN][FULL_FAIL] because flag remained invalid.
  - source lines in raw log: EURUSD L112, L113, L115, L120.
- EURJPY second cycle: [STRUCT][DIR][SOURCE] + [STRUCT][IMPULSE][RECENT_OK] + [STRUCT][PULLBACK][STANDARD_OK] then [STRUCT][CHAIN][FULL_FAIL].
  - source lines in raw log: EURJPY L112, L113, L115, L118.

## Pipeline reach observations
- Pipeline reaches [PIPE][ENTRY_ROUTER_PASS] and [ROUTER][RANK_ONLY][NO_WINNER], then ends with [DECISION][REJECT_FINAL][CODE=ROUTER_NO_CANDIDATE] in representative files.
