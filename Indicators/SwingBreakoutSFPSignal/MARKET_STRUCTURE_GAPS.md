# Notes — SwingBreakoutSFPSignal / SwingBreakoutTrader

Implementation notes on the current codebase relative to `STRATEGY.md`,
which is the complete, deliberately scoped specification for this pair
(level-reaction SFP/B&R + an A-B-C-D fib-extension entry). Its Status
section is explicit that nothing beyond what it describes is in scope -
these are observations about the existing build, not a backlog.

## Trend filter is SMA-based

`RequireTrendFilter` gates entries on a 20/200 SMA pair on `TrendTimeFrame`
(`TrendFilterOK` in `SwingBreakoutSFPSignal.cs`). An alternative would be to
derive direction from the swing sequence itself (higher-highs/higher-lows
vs. lower-highs/lower-lows) instead of a moving average, reusing the same
swing-detection machinery the A-B-C-D sequence already runs rather than a
second, unrelated indicator family. Worth considering if the SMA filter
ever needs replacing, not a defect in the current design.

## Only one active swing is tracked per side

The indicator remembers "the current unbroken swing high" and "the current
unbroken swing low," full stop - no history of prior swings. Per
STRATEGY.md's implementation notes this is intentional for the A-B-C-D
sequence (A/B are tracked as running extremes, not fixed anchors), so it's
not something to fix without a specific reason to want more history.

## A swing-level break already drives entry

B&R detection (`FindBrokenLevel`/`ProcessBnR`) fires on a close beyond
`lastSwingHigh`/`lastSwingLow` when `TrackPriorSwing` is on, exactly as it
does for PDH/PDL/PDC/HCOM/LCOM - a break of the tracked swing level is
already a valid trigger into the A-B-C-D sequence, not just bookkeeping.
