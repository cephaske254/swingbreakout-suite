# Trading Strategy — as described by the user

This file captures the complete trading approach the user described, in
their own terms, as the basis for reworking the indicator/bot pair.

## What this detects

A confirmed swing pivot is the only trigger — there is no level-reaction
(SFP/B&R), PDH/PDL/PDC, or HCOM/LCOM step anymore. A fresh A→B→C→D→E
attempt starts directly from any confirmed swing high (bearish) or swing
low (bullish).

## Chart timeframe

The user trades the **1-minute chart**. The indicator runs swing detection
and ATR against whatever timeframe the chart (`Bars`) is attached to, so
running it on M1 already works.

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

There are two independent lookback windows:
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

1. **A** = a confirmed Major swing low.
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

The Robot only acts on a signal whose **A and D both fall within the New
York session (13:00–22:00 UTC), on the same calendar day** — not just the
bar the signal happens to fire on, which can be well after D itself.
**Enable London Session** additionally allows London (08:00–17:00 UTC).
Both windows are fixed UTC hours, an approximation that drifts by about an
hour off the true session with the US/UK daylight-saving change (they
don't change on the same dates).

The indicator itself resets any in-progress A→B→C→D setup at a UTC day
boundary — each day gets its own state, so a setup can never carry over
past midnight UTC. This guarantees A and D always land on the same
calendar day whenever a signal does fire, which the session check above
relies on.

## Status

This is the complete strategy as described by the user. No level reaction,
SFP/B&R, PDH/PDL/PDC, HCOM/LCOM, or micro-structure-shift confluence is part
of it anymore — a confirmed swing pivot alone starts the A→B→C→D→E sequence.
No additional risk management or filters beyond what's written above and
the trading session rule.

## Implementation notes

This strategy is implemented in `SwingBreakoutSFPSignal.cs` (detection) and
`Swingbreakouttrader.cs` (execution). A few implementation decisions were
made that weren't explicitly specified and are worth reviewing:

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
  defaults to **Normal** - the user didn't specify a default.
- The Robot's stop and target are used exactly as the indicator computes
  them - no ATR buffer or minimum/maximum distance is layered on top, since
  the strategy fully specifies both. The one exception is `MinRiskRewardRatio`
  (default 1.7, 0 = off): since D's extension distance past the 100% break
  varies setup to setup, the resulting reward:risk isn't fixed by the A→B→C
  math alone, so the Robot filters out (does not enter) any setup whose
  reward:risk at E falls short of this ratio, rather than adjusting the
  stop/target themselves.
