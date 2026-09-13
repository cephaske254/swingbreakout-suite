# Our Notes — SwingBreakoutSFPSignal / SwingBreakoutTrader

These are our implementation notes on the current codebase relative to
`STRATEGY.md`, our complete, deliberately scoped specification for this pair
(level-reaction SFP/B&R + an A-B-C-D fib-extension entry). Its Status section
explicitly keeps everything beyond it out of scope, so we treat these as
observations about the existing build rather than a backlog.

## Only one active swing is tracked per side

Our indicator remembers "the current unbroken swing high" and "the current
unbroken swing low," full stop — it keeps no history of prior swings. As our
implementation notes in `STRATEGY.md` explain, this is intentional for the
A-B-C-D sequence: we track A/B as running extremes, not fixed anchors. We
would not change that without a specific reason to retain more history.

## A swing-level break already drives entry

Our B&R detection (`FindBrokenLevel`/`ProcessBnR`) fires when price closes
beyond `lastSwingHigh`/`lastSwingLow` while `TrackPriorSwing` is on, just as
it does for PDH/PDL/PDC/HCOM/LCOM. A break of the tracked swing level already
validly triggers the A-B-C-D sequence; we do not treat it as mere
bookkeeping.
