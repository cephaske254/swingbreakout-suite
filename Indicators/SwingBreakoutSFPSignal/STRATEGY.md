# Our Trading Strategy

This file captures the complete trading approach we use as the basis for
the indicator/robot pair.

## What this detects

A confirmed swing pivot is our only trigger — we do not use a level-reaction
(SFP/B&R), PDH/PDL/PDC, or HCOM/LCOM step anymore. A fresh A→B→C→D→E
attempt starts directly from any confirmed swing high (bearish) or swing
low (bullish).

## Chart timeframe

We trade the **1-minute chart**. Our indicator runs swing detection and ATR
against the timeframe attached to the chart (`Bars`), so it works on M1.

## How a swing pivot is detected — and why it auto-scales to any swing size

A bar at index `i` is a confirmed swing high/low once `left` bars before it
and `right` bars after it are all strictly less extreme — a pure shape
comparison over a fixed *bar-count* window, with no fixed price or ATR
distance involved anywhere in the test. That's what makes the same
detection logic find a 5-pip swing and a 500-pip swing identically: it's
asking "is this bar more extreme than its N neighbors on each side," not
"did price move X amount." A quiet, low-volatility instrument and a
fast-moving one both produce swings the pivot test finds the same way, at
whatever size those swings actually are.

We use two independent lookback windows:
- **Minor** (`SwingLeftBars`/`SwingRightBars`, default 5/5) — used to find
  B, C, and D once a setup is underway, and for the fine-grained
  Confidence Dots tier.
- **Major** (`MajorSwingLeftBars`/`MajorSwingRightBars`, default 15/15) —
  used to find **A**, the trigger. A 5/5 fractal test alone fires on nearly
  every noise-sized wiggle, which would spawn a fresh conflicting setup on
  almost every ranging stretch. Requiring more neighboring bars to agree
  before A is accepted filters that out while staying scale-relative (it's
  still a bar-count shape test, not a fixed price/ATR threshold) — so it
  finds big and small *major* swings the same way, it just won't fire on
  minor ones.

## Entry sequence (buy setup — mirror for sell)

1. **A** = our confirmed Major swing low.
2. **B** = the top of the first leg up from A (the high before the first
   retracement starts).
3. **C** = the bottom of the first retracement (where the pullback ends and
   price turns back up). The retracement must reach at least 50% of A→B:
   C is at or below the A/B midpoint for a buy (mirrored above it for a
   sell).
4. Draw a Fibonacci **extension** using A → B → C. Wait for price to break
   past the **100% extension level** (this sits above B — it's C plus the
   full A→B leg length, not just a new high above B).
5. Track the post-break extreme as **D**. It is finalized only when the
   opposite retracement pivot confirms (a pullback low after a bullish D, or
   pullback high after a bearish D), and that confirmation must occur within
   `Max. Bars from 100% Break to D` (80 by default).
6. Draw a standard Fibonacci **retracement** tool from D to C.
7. **E** is the 50% D→C retracement level. Place a **buy limit / sell limit
   order**, rather than entering immediately, at E.
   - When **Show A-B-C-D Structure** is enabled, the indicator draws the
     A→B→C Fibonacci expansion: A, B, and C are its three anchors and its
     extension levels are projected from C. This is visual-only; it does not
     alter the entry, stop, or target.
8. **Stop-loss**:
   - **Conservative mode** → SL at **C**.
   - **Normal mode** → SL at **A**.
9. **Target**: the **261.8%** A→B→C expansion, projected from C:
   **Target = C + (B − A) × 2.618**

Sell setups mirror all of the above (A = Major swing high, B = bottom of first leg
down, C = top of first retracement, D = new low past the 100% expansion, E
= the 50% D→C limit-order level, SL above C or A, target below C at the same
2.618x A→B→C expansion).

![Entry sequence diagram: A/B/C/D, the 100% extension, and the second retracement](../../strategy-entry-diagram.svg)

## Trading session

Our robot only acts on a signal whose **A and D both fall within the New
York session (13:00–22:00 UTC), on the same calendar day** — not merely the
bar on which the signal fires, which can be well after D itself. **Enable
London Session** also allows London (08:00–17:00 UTC). We use fixed UTC
hours for both windows, so they can drift by about an hour from the true
session when US and UK daylight-saving changes occur on different dates.

Our indicator resets every in-progress A→B→C→D setup at the UTC day
boundary. Each day has its own state, so a setup cannot carry past midnight
UTC. This ensures A and D always land on the same calendar day when a
signal fires, which our session check relies on.

## Status

This is our complete strategy. We do not include level reaction, SFP/B&R,
PDH/PDL/PDC, HCOM/LCOM, or micro-structure-shift confluence: a confirmed
swing pivot alone starts the A→B→C→D→E sequence. We use no additional risk
management or filters beyond what is written above and the trading-session
rule.

## Implementation notes

We implement this strategy in `SwingBreakoutSFPSignal.cs` (detection) and
`Swingbreakouttrader.cs` (execution). We made a few implementation decisions
that were not explicitly specified:

- **Nearest structure**: A is fixed to the triggering pivot bar. B is the
  first valid same-direction pivot after A, and C is the first valid
  retracement pivot after B; later, more extreme pivots do not stretch an
  in-progress setup into a longer swing.
- **Overlapping setups (cTrader)**: up to three active A→B→C→D sequences are
  retained independently in each direction. A new triggering pivot uses an
  idle slot and does not discard an older valid setup. When a newer
  completed setup passes all execution filters, its pending limit order
  replaces an older same-direction pending order, so only the most recent
  order can fill.
- **Entry order**: once D confirms beyond the 100% expansion, the Robot
  places a limit order at E, the 50% D→C retracement. It does not enter at
  market; the order fills only if price revisits E. If price returns to D
  first, the pending order is cancelled so it cannot trigger later.
- **Invalidation**: at any point after A is set, a close back through A
  abandons the whole attempt (not just a single stalled phase).
- **Drawing follows the sequence live, and disappears if it fails**: A, B,
  and C (plus the A→B→C expansion ratio lines, which don't depend on D) are
  drawn on the chart as soon as each one confirms, not only once the whole
  sequence completes. If an attempt never reaches a signal - A gets broken,
  D times out, or D confirms but the bar ordering comes out invalid - every
  object that attempt drew is erased rather than left on the chart. Only a
  sequence that actually produces a signal keeps its A→B→C→D→E drawing
  permanently.
- **Stop-Loss Mode** (`StopMode`: Conservative = SL at C, Normal = SL at A)
  defaults to **Normal**, our chosen default.
- The Robot's stop and target are used exactly as the indicator computes
  them - no ATR buffer or minimum/maximum distance is layered on top, since
  the strategy fully specifies both. The one exception is `MinRiskRewardRatio`
  (default 1.7, 0 = off): since D's extension distance past the 100% break
  varies setup to setup, the resulting reward:risk isn't fixed by the A→B→C
  math alone, so the Robot filters out (does not enter) any setup whose
  reward:risk at E falls short of this ratio, rather than adjusting the
  stop/target themselves.
