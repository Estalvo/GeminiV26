# XAUUSD TP1 Distance Audit (2026-03-30)

## Scope
Audited only XAUUSD path: entry -> SL distance -> TP1 price.

## Full calculation path

1. **Executor asks RiskSizer for SL distance**
   - `slPriceDist = CalculateStopLossPriceDistance(...)`.
2. **RiskSizer computes SL distance**
   - Loads M5 bars only for count check (`GetBars(TimeFrame.Minute5)`), but ATR is read from `bot.Indicators.AverageTrueRange(14, Exponential)` (no bars/timeframe argument).
   - Formula: `slDistance = atr * atrMult + 0.25`.
3. **Executor converts distance to SL pips and sends market order**
   - `slPips = slPriceDist / Symbol.PipSize` and passes it to `ExecuteMarketOrder`.
4. **Executor finalizes TP1 from fill price and realized SL distance**
   - `rDist = abs(entryPrice - slPriceActual)`.
   - `tp1Price = entry ± (rDist * tp1R)`.
5. **TP1R source**
   - `GetTakeProfit(...)` hard-sets `tp1R = 0.45` for XAUUSD.

## Numeric reconstruction (example)
Given:
- `entry = 4516.73`
- `tp1   = 4515.07`

Derived:
- `tp1Distance = |4516.73 - 4515.07| = 1.66`
- If `tp1R = 0.45`, then `slDistance ~= 1.66 / 0.45 = 3.6889`

Back-solving ATR by multiplier branch:
- If `atrMult = 2.2` -> `atr ~= (3.6889 - 0.25) / 2.2 = 1.5631`
- If `atrMult = 2.6` -> `atr ~= 1.3227`
- If `atrMult = 3.0` -> `atr ~= 1.1463`

This is consistent with **small ATR input driving small SL, then small TP1 (0.45R)**.

## Root cause classification
**A) ATR too small** (primary), driven by ATR source ambiguity/timeframe mismatch.

Why:
- Code comment says "XAU M5", and M5 bars are fetched, but ATR is not tied to those M5 bars.
- ATR call omits bars/timeframe, so effective ATR source follows robot/chart context rather than explicit M5.
- On lower/noisier chart context, ATR can be materially smaller -> SL shrinks -> TP1 shrinks proportionally.

## Additional findings

- **Multiplier branch is not abnormally low**: returns 2.2 / 2.6 / 3.0 only.
- **TP1 formula itself is correct** (`entry ± rDist*tp1R`).
- **No obvious XAU pip/tick shrink in TP1 path**: TP1 is computed directly in price space from realized `rDist`, not rounded to pip/tick before check.
- **Missing safeguards (critical operational gap)**:
  - No XAU-specific minimum SL distance floor (price units).
  - No minimum TP1 distance floor.
  - No ATR sanity floor for XAU before using ATR in SL formula.
- **Logging gap**:
  - There is no single mandatory `[SL_CALC]` log containing ATR, timeframe, multiplier, and slDistance together.
  - There is no single mandatory `[TP_CALC]` log containing tp1R and tp1Distance at entry.

## Repeatability
Yes, repeatable.
- Any run where ATR resolves from too-small context can reproduce tight SL/TP1.
- Because TP1 is fixed at `0.45R`, any SL compression linearly compresses TP1.

## Minimal fix recommendation (no refactor)
1. Bind ATR calculation explicitly to M5 bars in XAU risk sizing.
2. Add lightweight guards:
   - minimum `slDistance` (XAU price units),
   - minimum `tp1Distance` (derived from `slDistance * tp1R`),
   - ATR sanity floor for XAU.
3. Add entry-time audit logs:
   - `[SL_CALC] symbol, atr, atr_tf, multiplier, slDistance, source`
   - `[TP_CALC] symbol, tp1R, tp1Price, tp1Distance`

