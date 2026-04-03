# CLEAN RESCUE PLAN (03.27 BASELINE-FIRST)

## 1) OVERALL RESCUE VERDICT
- **Baseline = 2026-03-27 snapshot** (`0b95daf`, `PHASE3.9_120260327.zip`).
- A mentési stratégia: **csak nem-entry jellegű, működő heti fejlesztések visszaemelése**, az entry pipeline teljes érintetlensége mellett.
- Emiatt a döntési elv:
  - Ami execution/risk/logging/rehydrate stabilitási javítás és **nem írja át az entry döntési láncot** → portolható.
  - Ami entry döntési láncot, hard veto réteget, qualificationt vagy structural authority-t érint → most nem portolható.

## 2) SAFE TO PORT

### Rehydrate full
- **Status: SAFE TO PORT**
- Indok: runtime újraindítási/állapot-visszatöltési réteg, nem entry jelgenerálás.
- Fájlok:
  - `Core/Runtime/RehydrateService.cs`
  - `Core/Runtime/BotRestartState.cs`
  - `Core/RuntimeSymbolResolver.cs`
  - `Instruments/*/*ExitManager.cs` (csak rehydrate recovery branch-ek)

### FX router fix / symbol normalization
- **Status: SAFE TO PORT**
- Indok: symbol routing és resolver normalizáció, nem entry scoring/pipeline.
- Fájlok:
  - `Core/SymbolRouting.cs`
  - `Core/RuntimeSymbolResolver.cs`

### Direction internal guard
- **Status: SAFE TO PORT**
- Indok: konzisztencia guard, amely védi a direction-hibákat, de nem helyettesíti az entry pipeline-t.
- Fájlok:
  - `Core/DirectionGuard.cs`
  - `Core/TradeCore.cs` (csak direction-guard hívási pontok)
  - `Instruments/*/*InstrumentExecutor.cs` (ahol a guard wiring történik)

### 3% daily DD
- **Status: SAFE TO PORT**
- Indok: globális risk guard (daily drawdown plafon), entrytől független kockázati korlát.
- Fájlok:
  - `Core/Risk/GeminiRiskConfig.cs`
  - `Core/Risk/GlobalRiskGuard.cs`
  - `Core/TradeCore.cs` (daily DD gate callsite)

### GlobalLogging
- **Status: SAFE TO PORT**
- Indok: megfigyelhetőség/auditability; nem entry logikai redesign.
- Fájlok:
  - `Core/Logging/GlobalLogger.cs`
  - `Core/Logging/RuntimeFileLogger.cs`
  - `Core/Logging/TradeAuditLog.cs`
  - `Core/Analytics/UnifiedAnalyticsWriter.cs`

### Instrument-specific fixek (nem-entry)
- **Status: SAFE TO PORT**
- Indok: végrehajtási/exites stabilitási patchek, ha nem nyúlnak entry kiválasztáshoz.
- Fájlok:
  - `Instruments/*/*ExitManager.cs` (safe modify, TP/SL management stabilitás)
  - `Instruments/*/*InstrumentExecutor.cs` (execution safety / logging)

## 3) NEEDS CAREFUL PORT

### HTF mismatch soft kezelés
- **Status: NEEDS CAREFUL PORT**
- Indok: többnyire entry környezetben él; csak a **soft treatment** vihető, hard block nem.
- Guardrail:
  - semmilyen új hard veto ne kerüljön be,
  - csak penalty / preferencia szintű kezelés maradjon.
- Érintett zónák:
  - `Core/Entry/*`
  - `EntryTypes/*`

### TVM finomítás
- **Status: NEEDS CAREFUL PORT**
- Indok: trade lifecycle réteg, de trigger timing és state-átadás kapcsolódhat entry állapothoz.
- Fájlok:
  - `Core/TradeViabilityMonitor.cs`

### TTM finomhangolás
- **Status: NEEDS CAREFUL PORT**
- Indok: trend menedzsment az entryt követő fázisban, de erős függés lehet context mezőktől.
- Fájlok:
  - `Core/TradeManagement/TrendTradeManager.cs`

### Trailing finomhangolás
- **Status: NEEDS CAREFUL PORT**
- Indok: alapvetően non-entry, de context mezők driftje regressziót okozhat.
- Fájlok:
  - `Core/TradeManagement/AdaptiveTrailingEngine.cs`
  - `Core/TradeManagement/TrailingProfiles.cs`
  - `Instruments/*/*ExitManager.cs` (csak trailing ágak)

## 4) DO NOT PORT NOW

### Automatikusan tiltott (explicit szabály)
- **FA**
- **Qualification**
- **current structural authority rework**
- **current entry pipeline**
- **current entry acceptance / hard veto layer**

### Konkretizált fájlzónák
- `Core/Entry/Qualification/EntryStateEvaluator.cs`
- `Core/Entry/EntryContextBuilder.cs` (authority rework részek)
- `Core/Entry/EntryContext.cs` (qualification/authority hard-veto mezők)
- `Core/TradeCore.cs` (FA/qualification/hard-veto callsite-ok)
- `Core/Entry/TransitionDetector.cs`, `Core/Entry/EntryRouter.cs`, `Core/Entry/EntryEvaluation.cs` **ha** a jelenlegi branch-specifikus entry redesign részeit hoznák
- `EntryTypes/*` azon részei, amelyek hard authority/qualification veto logikát építenek az entry-be

## 5) FILE-LEVEL PORT MAP

### SAFE TO PORT (közvetlenül emelhető)
- `Core/Runtime/RehydrateService.cs`
- `Core/Runtime/BotRestartState.cs`
- `Core/RuntimeSymbolResolver.cs`
- `Core/SymbolRouting.cs`
- `Core/DirectionGuard.cs`
- `Core/Risk/GeminiRiskConfig.cs`
- `Core/Risk/GlobalRiskGuard.cs`
- `Core/Logging/GlobalLogger.cs`
- `Core/Logging/RuntimeFileLogger.cs`
- `Core/Logging/TradeAuditLog.cs`
- `Core/Analytics/UnifiedAnalyticsWriter.cs`
- `Instruments/*/*InstrumentExecutor.cs` (non-entry safety/logging)
- `Instruments/*/*ExitManager.cs` (rehydrate + non-entry stable fixes)

### NEEDS CAREFUL PORT (szeletelve, guardrail mellett)
- `Core/TradeViabilityMonitor.cs`
- `Core/TradeManagement/TrendTradeManager.cs`
- `Core/TradeManagement/AdaptiveTrailingEngine.cs`
- `Core/TradeManagement/TrailingProfiles.cs`
- `Instruments/*/*ExitManager.cs` (trailing-only részhalmaz)
- `Core/Entry/*` és `EntryTypes/*` csak HTF soft mismatch finomítás, hard block nélkül

### DO NOT PORT NOW (tiltott réteg)
- `Core/Entry/Qualification/EntryStateEvaluator.cs`
- `Core/Entry/EntryContextBuilder.cs` (entry-authority redesign)
- `Core/Entry/EntryContext.cs` (qualification/hard veto állapot)
- `Core/TradeCore.cs` (FA + hard-veto + redesign entry orchestration részek)
- `Core/Entry/TransitionDetector.cs`, `Core/Entry/EntryRouter.cs`, `Core/Entry/EntryEvaluation.cs` (current entry pipeline változatai)
- `EntryTypes/*` minden qualification/authority hard veto ága

## 6) RECOMMENDED ORDER
1. **Baseline lock**: `0b95daf` (03.27) fixálása és csak erre dolgozás.
2. **Observability + risk first**: GlobalLogging + 3% DD + symbol normalization.
3. **Runtime resilience**: rehydrate full visszaemelése.
4. **Execution safety**: direction guard + instrument non-entry fixek.
5. **Trade management slice**: TVM/TTM/trailing csak kontrollált, regressziófigyeléses szeletekben.
6. **HTF soft mismatch**: kizárólag soft/penalty kezelés, hard veto tiltás.
7. **Final gate audit**: megerősítés, hogy FA/Qualification/entry authority rework/entry-végi hard veto **nem** került vissza.
