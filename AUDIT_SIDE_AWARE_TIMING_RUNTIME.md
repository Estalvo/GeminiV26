# Runtime Behavior Audit — Side-Aware Timing in `EntryContextBuilder.AttachMemorySnapshot`

## Scope audited
- `Core/Entry/EntryContextBuilder.cs` (`AttachMemorySnapshot`, side log lines)
- `Core/Entry/EntryContext.cs` (side-aware timing fields + defaults)
- `Gemini/Memory/MarketMemoryEngine.cs` (source semantics for timing values)
- `Gemini/Memory/SymbolMemoryState.cs` (source state model)
- `Core/TradeCore.cs` (memory sync behavior and EntryLogic interaction boundary)

---

## 1) RUNTIME BEHAVIOR SUMMARY
**Verdict: PARTIAL**

Observed runtime assignment in `AttachMemorySnapshot` is correctly side-gated by `ImpulseDirection` and prevents mirrored LONG/SHORT copying. Every side-aware timing field uses either:
- active side: source memory value, or
- inactive side: neutral fallback (`false`, `-1`, or `0`) by ternary gating.

So the **gating fix works** for anti-mirroring.

However, several neutral values (`0`, `false`, and for one field even `-1`) are not uniquely distinguishable from possible real computed values on the active side. This creates interpretation risk if downstream logic consumes these fields without an explicit side-validity check.

---

## 2) ACTIVE VS INACTIVE SIDE MATRIX

Legend for neutralization target: `bool=false`, `int=-1`, `double=0`.

| Field | Long active (`ImpulseDirection>0`) | Short active (`ImpulseDirection<0`) | Inactive side value | Consistency |
|---|---|---|---|---|
| `HasFreshPullbackLong/Short` | active side gets `assessment.IsFirstPullbackWindow` | mirrored by side | `false` | YES |
| `HasEarlyContinuationLong/Short` | active side gets `assessment.IsEarlyContinuationWindow` | mirrored by side | `false` | YES |
| `HasLateContinuationLong/Short` | active side gets `assessment.IsLateContinuation` | mirrored by side | `false` | YES |
| `IsOverextendedLong/Short` | active side gets `assessment.IsOverextendedMove` | mirrored by side | `false` | YES |
| `BarsSinceStructureBreakLong/Short` | active side gets `state.BarsSinceBreak` | mirrored by side | `-1` | YES |
| `BarsSinceImpulseLong/Short` | active side gets `state.BarsSinceImpulse` | mirrored by side | `-1` | YES |
| `ContinuationAttemptCountLong/Short` | active side gets `state.ContinuationAttemptCount` | mirrored by side | `0` | YES *(neutral differs from requested int neutral target)* |
| `DistanceFromFastStructureAtrLong/Short` | active side gets `state.DistanceFromFastStructureAtr` | mirrored by side | `0` | YES |
| `ContinuationFreshnessLong/Short` | active side gets `state.ContinuationFreshnessScore` | mirrored by side | `0` | YES |
| `TriggerLateScoreLong/Short` | active side gets `state.TriggerLateScore` | mirrored by side | `0` | YES |

### Critical note on requested neutral definition
User-defined neutral int target is `-1`. `ContinuationAttemptCountLong/Short` neutralizes to `0`, not `-1`. This is a **real inconsistency vs requested neutral policy** (even though it is internally consistent in code).

---

## 3) NEUTRAL VALUE RISK TABLE

| Field(s) | Neutral used | Misinterpretation possible? | Risk | Why |
|---|---:|---|---|---|
| `HasFreshPullback*`, `HasEarlyContinuation*`, `HasLateContinuation*`, `IsOverextended*` | `false` | YES | MEDIUM | `false` can mean either “inactive side” or “active side computed false”. |
| `BarsSinceImpulse*` | `-1` | YES | LOW | Inactive uses `-1`; active source appears non-negative in memory lifecycle, so generally distinguishable, but only if consumers enforce `>=0` validity check. |
| `BarsSinceStructureBreak*` | `-1` | YES | HIGH | Memory state itself allows real `BarsSinceBreak=-1` (e.g., not established), so inactive and active-not-established collapse to same value. |
| `ContinuationAttemptCount*` | `0` | YES | HIGH | `0` is a normal active value (first attempt) and also inactive neutral. |
| `ContinuationFreshness*` | `0` | YES | HIGH | Real computed score range is `[0..1]`; `0` can be real exhaustion or neutralized inactive. |
| `TriggerLateScore*` | `0` | YES | HIGH | Real computed score range is `[0..1]`; `0` can be truly early/clean or neutralized inactive. |
| `DistanceFromFastStructureAtr*` | `0` | YES | HIGH | Real formula can produce exact `0` when close equals anchor; also initialized as `0`. Inactive also `0`. |

---

## 4) DEFECTS (RUNTIME)

Only observable issues from current code behavior:

1. **Neutral policy mismatch for int field**
   - **Field:** `ContinuationAttemptCountLong`, `ContinuationAttemptCountShort`
   - **Condition:** inactive side assignment path in `AttachMemorySnapshot`
   - **Observed behavior:** uses `0` instead of requested neutral int sentinel `-1`
   - **Why this breaks logic:** `0` is a valid active-side value, so inactive side can look like “fresh first attempt”
   - **Trading consequence:** timing filters could under-penalize or incorrectly accept entries if a consumer reads attempt count without side validity guard.

2. **Active/inactive ambiguity on key doubles**
   - **Field:** `DistanceFromFastStructureAtr*`, `ContinuationFreshness*`, `TriggerLateScore*`
   - **Condition:** inactive side forced to `0`; active side may also legitimately compute `0`
   - **Observed behavior:** value equality between neutral and real computed states
   - **Why this breaks logic:** value-only consumers cannot distinguish “not applicable” vs “computed weak/zero-distance”
   - **Trading consequence:** false confidence or false penalties depending on interpretation.

No partial-side leakage (e.g., inactive side receiving non-neutral numeric from active memory) was observed in assignment logic; all side-aware fields are explicitly gated.

---

## 5) OVEREXTENSION / LATE RELATION

**Independent? NO (partially coupled).**

Evidence from memory rules:
- `HasLateContinuation*` derives from `assessment.IsLateContinuation`, which is true only when `ContinuationWindowState == Late`.
- `IsOverextended*` derives from `assessment.IsOverextendedMove`, which is true when `MoveExtensionState == Overextended`.
- `ResolveContinuationWindowState` forces `Overextended` moves into `Exhausted` window before the `Late` branch.

Practical consequence:
- `Late=true` and `Overextended=true` at the same time is structurally blocked by window resolution ordering.
- `Late=false` and `Overextended=false` can occur together (Fresh/Early/Mature normal extension).

So these flags are not equivalent, but they are **not fully independent** in runtime state transitions.

---

## 6) DISTANCE FIELD VALIDITY

**Directional? YES (assignment path), but value semantics are PARTIAL.**

- Directional gating works (inactive side set to `0`).
- But `0` is also a valid active-side computed value from `abs(close-anchor)/atr` and from initialization/reset flows.

**Ambiguity risk: HIGH.**

---

## 7) ENTRYLOGIC RISK

**Status: CONDITIONAL**

- Current repository scan shows side-aware timing fields are written in `EntryContextBuilder` and declared in `EntryContext`, but there is no direct observed consumption of these specific side-aware fields in instrument `EntryLogic` classes at present.
- Therefore immediate production misread risk is limited *today* if those fields are not used.
- If EntryLogic starts consuming them without checking active side (`ImpulseDirection`/route direction), neutral values can be interpreted as real timing signals.

Concrete misread examples if consumed naively:
- `TriggerLateScoreShort=0` during long impulse could be read as “excellent short timing” instead of “inactive side neutralized”.
- `ContinuationAttemptCountShort=0` during long impulse could be read as “first short continuation attempt”.
- `DistanceFromFastStructureAtrShort=0` during long impulse could be read as “perfect structure proximity”.

---

## 8) MINIMAL HARDENING SUGGESTIONS (max 3, no refactor)

Only defensive hardening suggestions; no architecture changes:

1. Add one explicit side-validity boolean per side in `EntryContext` snapshot (`IsTimingLongActive`, `IsTimingShortActive`) and log them in `[CTX][TIMING][SIDE]`.
2. Change neutral for `ContinuationAttemptCount*` from `0` to `-1` to align with int sentinel policy and avoid active/inactive collision.
3. Expand `[CTX][TIMING][SIDE]` log line to include `barsSinceImpulse`, `barsSinceBreak`, `attempts`, `triggerLate`, and `distanceAtr` so audits can verify neutralization from runtime logs directly.

---

## F) LOG CONSISTENCY CHECK (explicit answer)

Current `[CTX][TIMING][SIDE]` logs show only:
- side label
- `early`, `late`, `overextended`, `freshness`

They do **not** show:
- source active side (`ImpulseDirection`)
- bars counters (`BarsSinceImpulse*`, `BarsSinceStructureBreak*`)
- attempt count
- trigger-late score
- distance ATR

Conclusion: logs are helpful but **insufficient to prove full side-neutralization correctness** for all timing fields.

---

## B) PARTIAL SIDE LEAKAGE (explicit answer)

No partial-side leakage found in `AttachMemorySnapshot` assignments: every side-aware field is explicitly gated with the same directional checks (`hasLongTiming`, `hasShortTiming`) and sets inactive side to fallback neutral.

---

## A) ACTIVE SIDE VALIDATION (explicit answer)

- For `ImpulseDirection > 0`: long side receives source values, short side receives neutral fallback for every side-aware timing field.
- For `ImpulseDirection < 0`: short side receives source values, long side receives neutral fallback for every side-aware timing field.
- Exception vs requested neutral convention: `ContinuationAttemptCount*` uses `0` instead of int sentinel `-1`.

