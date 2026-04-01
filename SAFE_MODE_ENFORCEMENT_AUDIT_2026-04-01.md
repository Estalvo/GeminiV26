# GEMINI V26 – SAFE MODE Enforcement Audit
Date: 2026-04-01 (UTC)
Scope: TradeCore, PositionContext, EntryLogic/EntryRouter, ExitManager(s), RiskSizer(s), TVM, trailing, TP1/TP2, RehydrateService, Executor.
Mode: Audit-only (no runtime logic changes).

## 1) Overall Compliance Score
**7/10**

System-level SAFE MODE intent is mostly present (rehydrate marks, confidence zeroing, TVM skip guards in exit managers), but at least one **hard policy leak** remains in XAU rehydrate TP1 policy, and one telemetry-level mismatch can mask SAFE MODE state.

## 2) Critical Violations

### CRITICAL-1
- **File:** `Instruments/XAUUSD/XauExitManager.cs`
- **Method:** `RehydrateFromLivePositions`
- **Pattern:**
  - `RehydratedWithoutConfidence = true;`
  - `ctx.ComputeFinalConfidence();`
  - `ctx.Tp1R = _profile.Tp1R_Normal;` (comment says "nincs FC, ezért normal bucket")
- **Issue:** SAFE MODE (rehydrated without confidence) should force conservative/fixed TP1 handling, but code explicitly assigns **normal bucket TP1** in SAFE MODE path.
- **Severity:** **CRITICAL**

## 3) Warnings

### WARN-1 (Telemetry mismatch)
- **File:** `Core/Logging/TradeAuditLog.cs`
- **Method:** `BuildEntrySnapshot`
- **Pattern:** `adjustedRiskConfidence={ctx.FinalConfidence}`
- **Issue:** Adjusted risk confidence field is logged from `FinalConfidence` instead of `AdjustedRiskConfidence`. In SAFE MODE, `AdjustedRiskConfidence` is forced to `0`, but telemetry can still show non-zero adjusted confidence.
- **Impact:** Can hide SAFE MODE enforcement status in audits/analytics.

### WARN-2 (No centralized SAFE MODE API)
- **Pattern searched:** `IsSafeMode()`
- **Result:** no matches.
- **Issue:** Guarding is done ad-hoc with `ctx.RehydratedWithoutConfidence` across modules; absence of unified helper increases drift risk.

## 4) Full Finding List

1. **XAU SAFE MODE TP1 policy leak**
   - file: `Instruments/XAUUSD/XauExitManager.cs`
   - method: `RehydrateFromLivePositions`
   - exact pattern: SAFE MODE context + `Tp1R_Normal` assignment.
   - why error: SAFE MODE should avoid non-conservative confidence-derived profile assumptions.

2. **Adjusted-risk telemetry leak**
   - file: `Core/Logging/TradeAuditLog.cs`
   - method: `BuildEntrySnapshot`
   - exact pattern: `adjustedRiskConfidence={ctx.FinalConfidence}`
   - why error: field semantics are wrong in SAFE MODE and can conceal confidence disablement.

3. **No `IsSafeMode()` helper detected**
   - global scan result: no implementation/use.
   - why warning: policy enforcement consistency relies on manual checks.

## 5) SAFE MODE Coverage Matrix

| Module | Status | Notes |
|---|---|---|
| PositionContext | OK | SAFE MODE forces `AdjustedRiskConfidence = 0` in `ComputeFinalConfidence()`. |
| RehydrateService | OK | Sets `RehydratedWithoutConfidence = true` and logs `[REHYDRATE][SAFE_MODE]`. |
| ExitManager (global pattern) | OK* | TVM early-exit guarded by `if (ctx.RehydratedWithoutConfidence) ... else if (_tvm.ShouldEarlyExit(...))`. |
| ExitManager XAU rehydrate TP1 | FAIL | SAFE MODE path sets `Tp1R_Normal` explicitly. |
| RiskSizer | OK* | Uses confidence inputs, but SAFE MODE zeroing from PositionContext exists; no direct leak found in sizers themselves. |
| TVM | OK* | Called via guarded branches in instrument exit managers; no confidence inputs detected in TVM logic. |
| Trailing | OK | No confidence-dependent trailing branch detected in SAFE MODE paths; post-TP1 management appears structure/price-driven. |
| TP1/TP2 handling | PARTIAL | TP1 resolver has SAFE MODE conservative branch; exception in XAU rehydrate bootstrap assignment. |
| Executor | OK* | Confidence-based sizing for new entries; SAFE MODE concerns mainly rehydrated lifecycle, not new-entry path. |
| Logging | PARTIAL | `[REHYDRATE][SAFE_MODE]` and `[EXIT][SAFE_MODE]` present; `[CTX][SAFE_MODE]` exists in state snapshot, but adjusted-risk field mismatch remains. |

(
`*` = compliant by inspected code paths; still dependent on calling-context integrity.)

## 6) Required Minimal Fixes (no refactor)

1. **Fix CRITICAL-1** (`Instruments/XAUUSD/XauExitManager.cs`)
   - Replace SAFE MODE rehydrate TP1 bootstrap from normal to conservative.
   - Minimal patch intent:
     - `ctx.Tp1R = _profile.Tp1R_Low;`
     - optional explicit SAFE MODE audit log confirming conservative TP1 bootstrap.

2. **Fix WARN-1** (`Core/Logging/TradeAuditLog.cs`)
   - Correct telemetry field mapping:
     - `adjustedRiskConfidence={ctx.AdjustedRiskConfidence}`

3. **Hardening (recommended)**
   - Add `bool IsSafeMode() => RehydratedWithoutConfidence` in `PositionContext` and migrate checks incrementally (non-breaking).

