# Trading Strategy — as described by the user

This file captures the complete trading approach the user described, in
their own terms, as the basis for reworking the indicator/bot pair.

## Levels marked on the chart

- **PDH / PDL / PDC** — previous day's high, low, and close.
- **HCOM / LCOM** — highest and lowest *daily close* within the current
  month (month-to-date), not intraday high/low.

(Weekly/Monthly Opening Range and the higher-timeframe swing high/low were
in the original indicator but are not part of the user's method — removed.)

## Chart timeframe

The user trades the **1-minute chart**. This matters for detection, not just
preference: a break → retest → reversal sequence that plays out as several
distinct 1-minute bars can collapse into what *looks like* a single wick on
a 15-minute or 1-hour chart — the higher timeframe hides the exact
mechanics. SFP and B&R must therefore be detected on the actual execution
timeframe (1-minute), not inferred from a coarser one.

The indicator already supports this structurally: swing detection, SFP/B&R
detection, and ATR all run against whatever timeframe the chart (`Bars`) is
attached to, so running it on M1 already works. PDH/PDL/PDC/HCOM/LCOM are
still correctly pulled from daily bars regardless of the chart's own
timeframe.

## What happens when price visits a level

When price visits one of the marked levels, one of two outcomes plays out:

- **SFP (Swing Failure Pattern)** — price breaks through the level (a wick/
  sweep), fails to hold beyond it, and reverses to the opposite side.
- **B&R (Break & Retest)** — price breaks through the level, pulls back to
  retest it, then continues in the breakout direction.

## How SFP and B&R are detected

Both are evaluated per bar against every enabled level (PDH/PDL/PDC,
HCOM/LCOM, swing high/low), using the same `MinSweepDepthATRmult × ATR`
distance threshold so a bare tick through the level doesn't count.

### SFP (Swing Failure Pattern) — single bar

- **Bullish SFP** at a level: the bar's low trades at least
  `MinSweepDepthATRmult × ATR` below the level, and the same bar's close is
  back above the level.
- **Bearish SFP**: mirrored (high sweeps above the level by the threshold,
  close ends back below it).

One bar is enough — the wick-through and the close-back-through both happen
on the same bar.

![SFP detection diagram](../../sfp-detection-diagram.svg)

### B&R (Break & Retest) — two bars minimum, no fixed window

1. **Break bar**: a bar *closes* beyond the level by at least
   `MinSweepDepthATRmult × ATR` (bullish: close above the level + threshold;
   bearish: mirrored). This is a real break, not a wick — the level gave way.
   The level is now "pending" for this direction.
2. **Retest (unbounded in time)**: from that point on, watch for a
   **retest bar** — one whose wick comes back and touches the level
   (bullish: low ≤ level) while its **close stays on the breakout side**
   (bullish: close still above the level). That means the level held as new
   support/resistance instead of being reclaimed. There's no fixed number of
   bars to wait — retests can take anywhere from a couple of bars to a long
   stretch, so the pending break just stays open until it resolves one way
   or the other (see the invalidation rule below).
3. Once a retest bar appears, **B&R is confirmed on that bar** — it becomes
   the trigger point (same role as the SFP bar) that establishes direction
   and kicks off the A→B→C→D sequence below.
4. **Invalidation**: if price closes back through the level in the failure
   direction before a valid retest happens, the pending break is cleared —
   no B&R is registered for it, and the level goes back to being
   unbroken/available for a fresh break or SFP later.

![B&R detection diagram](../../bnr-detection-diagram.svg)

## Entry sequence (buy setup — mirror for sell)

Once a level has been identified as held (B&R) or failed (SFP) and direction
is established:

1. **A** = the current/recent swing low.
2. **B** = the top of the first leg up from A (the high before the first
   retracement starts).
3. **C** = the bottom of the first retracement (where the pullback ends and
   price turns back up).
4. Draw a Fibonacci **extension** using A → B → C. Wait for price to break
   past the **100% extension level** (this sits above B — it's C plus the
   full A→B leg length, not just a new high above B).
5. That breakout produces a **new high, D**.
6. Wait for the **second retracement** — the pullback that follows D. This
   is the retracement we care about; the entry is found here.
7. Draw a standard Fibonacci **retracement** tool anchored on **D and C**.
8. **Entry** at the **50% retracement** of D–C.
9. **Stop-loss**:
   - **Conservative mode** → SL at **C**.
   - **Normal mode** → SL at **A**.
10. **Target**: the **261.8%** extension of the same C→D leg, projected
    beyond D:
    **Target = C + (D − C) × 2.618**

Sell setups mirror all of the above (A = swing high, B = bottom of first leg
down, C = top of first retracement, D = new low, SL above C or A, target
below D by the same 2.618x C→D leg projection).

![Entry sequence diagram: A/B/C/D, the 100% extension, and the 50% D→C entry](../../strategy-entry-diagram.svg)

## Status

This is the complete strategy as described by the user. No additional risk
management, session/timing rules, or filters beyond what's written above.

## Implementation notes

This strategy is implemented in `SwingBreakoutSFPSignal.cs` (detection) and
`Swingbreakouttrader.cs` (execution). A few implementation decisions were
made that weren't explicitly specified and are worth reviewing:

- **B&R retest window**: unbounded, per the user (see above) - a break stays
  pending until it's retested or invalidated, with no bar-count limit.
- **B&R level tracking**: a single pending-break tracker per direction
  (bullish/bearish), not one per individual level type - the first enabled
  level whose break condition fires arms it. Mirrors how SFP already ORs
  across enabled levels rather than tracking each independently.
- **A, during "seeking B"**: tracked as the running extreme (lowest low for
  a bullish setup) since the trigger bar, rather than fixed at the trigger
  bar's own extreme - avoids getting stuck on a stale anchor if price makes
  a lower low before the first leg actually starts.
- **B, during "seeking C"**: if a new, more extreme pivot forms before a
  retracement is confirmed, B is extended to it rather than treated as the
  final leg top.
- **Entry trigger**: fires the bar price *wicks* to the 50% D→C level (not a
  close), same convention as the SFP wick checks elsewhere in the indicator.
  The Robot still enters at market on the next tick after the signal bar
  closes - this is not a resting limit order at the 50% level.
- **Invalidation**: at any point after A is set, a close back through A
  abandons the whole attempt (not just a single stalled phase).
- **Stop-Loss Mode** (`StopMode`: Conservative = SL at C, Normal = SL at A)
  defaults to **Normal** - the user didn't specify a default.
- The Robot's stop and target are used exactly as the indicator computes
  them - no ATR buffer, minimum/maximum distance, or Risk:Reward hierarchy
  is layered on top anymore, since the strategy fully specifies both.
