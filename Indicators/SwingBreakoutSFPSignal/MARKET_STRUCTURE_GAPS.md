# Market Structure Gaps — SwingBreakoutSFPSignal / SwingBreakoutTrader

Review notes on where the current indicator/bot pair falls short of a genuine
market-structure trading approach. Written before any redesign work, to serve
as the basis for a follow-up discussion.

## 1. Trend/regime is SMA-based, not structure-based

`RequireTrendFilter` gates entries on a 20/200 SMA pair on the higher
timeframe. That's indicator-based trend-following, not market structure. A
structure-driven approach should derive trend from the sequence of swings
themselves (higher-highs/higher-lows = bullish structure, lower-highs/
lower-lows = bearish), not from a moving average. The SMA and the
swing-structure logic never talk to each other today.

## 2. BOS/CHoCH exists internally but isn't used as a signal

`swingHighBroken`/`swingLowBroken` already detect a break of structure, but
only as bookkeeping to decide which pivot is allowed to replace the tracked
swing. There's no CHoCH detection (first break in the *opposite* direction of
the prevailing structure, signaling a possible reversal) and no entry trigger
built directly off a confirmed BOS.

## 3. Only one swing is tracked per side — no structure history

The indicator remembers "the current unbroken swing high" and "the current
unbroken swing low," full stop. There's no sequence (HH → HL → HH ...) to
classify the trend regime or detect a shift, and no concept of "internal"
(minor) vs. "external" (major) structure — everything uses the same
`SwingLeftBars`/`SwingRightBars`.

## 4. Levels are fixed reference points, not liquidity concepts

PDH/PDL, weekly/monthly OR, HCOM/LCOM are all legitimate levels, but there's
no equal-highs/equal-lows detection (a core liquidity-pool concept) and no
order blocks or fair value gaps — the things price is actually drawn to /
rebalances against in a structure-based read.

## 5. Multi-timeframe structure is thin

Only one higher timeframe is used, and only for a single swing point plus the
SMA trend. A structural approach usually wants at least two layers (e.g.,
daily bias / 4H structure / entry-TF trigger) with explicit alignment, not
just "aware of one HTF pivot."

## 6. Premium/discount is underused

The Fib retracement filter (`RequireFibRetracement`) checks the 50–100% zone,
but it's optional/off by default and only gates entry — it isn't used to bias
targets or filter which liquidity levels are even eligible.

## Priority if addressed

1. BOS/CHoCH as an actual entry trigger
2. Structure-based trend instead of SMA
3. Equal-highs/equal-lows liquidity detection
4. (then) swing-sequence history, multi-timeframe alignment, premium/discount
   integration, order blocks / FVGs
