# Market Structure Gaps — SwingBreakoutSFPSignal / SwingBreakoutTrader

Originally written before the strategy redesign captured in `STRATEGY.md`, as
a review of where this pair fell short of a full ICT/SMC-style market-
structure system. That redesign has since happened: `STRATEGY.md` is now the
user's own complete, deliberately scoped specification (level-reaction
SFP/B&R + an A-B-C-D fib-extension entry), and its Status section is explicit
that nothing beyond what it describes is in scope. Most of the original list
below turned out to be describing concepts the user's method never wanted,
not oversights to fix - this revision separates the two.

## Still true today

**Trend/regime is SMA-based, not structure-based.** `RequireTrendFilter`
gates entries on a 20/200 SMA pair on `TrendTimeFrame` (`TrendFilterOK` in
`SwingBreakoutSFPSignal.cs`). A structure-driven trend read (higher-highs/
higher-lows vs. lower-highs/lower-lows from the swing sequence itself) would
be a different, and arguably more consistent, way to gate direction - the SMA
and the swing-structure logic still never talk to each other. This remains a
legitimate design option, not something STRATEGY.md rules out; it just isn't
what's built.

**Only one swing is tracked per side.** The indicator remembers "the current
unbroken swing high" and "the current unbroken swing low," full stop - no
sequence history (HH → HL → HH ...) and no internal/minor vs. external/major
structure distinction (everything uses the same `SwingLeftBars`/
`SwingRightBars`). Per STRATEGY.md's implementation notes, this is
intentional for the A-B-C-D sequence itself (A/B tracked as running extremes,
not fixed anchors) - but it does mean there's no broader "market structure"
picture beyond the single active swing on each side.

## Resolved, in spirit, since this was written

**A structural break now IS an entry trigger** - B&R detection
(`FindBrokenLevel`/`ProcessBnR`) fires on a close beyond `lastSwingHigh`/
`lastSwingLow` when `TrackPriorSwing` is on, exactly as it does for PDH/PDL/
PDC/HCOM/LCOM. That's a break of structure driving the A-B-C-D sequence,
which is most of what the original "BOS exists internally but isn't used as
a signal" gap was asking for. What's still missing is CHoCH specifically
(the first break in the *opposite* direction of the prevailing structure,
read as a reversal signal distinct from a same-direction continuation break)
- there's no concept of "prevailing structure direction" for a break to
oppose, since trend here comes from the SMA filter, not from structure.

## Out of scope by design, not a gap

These describe an ICT/SMC-style liquidity/structure system the user's
method (STRATEGY.md) never asked for. Listed here only so a future reader
doesn't mistake "not built" for "forgotten":

- Equal-highs/equal-lows liquidity pools, order blocks, fair value gaps -
  the user's levels are PDH/PDL/PDC, HCOM/LCOM, and swing high/low, full
  stop. Nothing else feeds the SFP/B&R reaction check.
- Multi-timeframe structural alignment (e.g. daily bias / 4H structure /
  entry-TF trigger) - the design uses exactly one higher timeframe, and
  only for the SMA trend filter, not for a second structural opinion.
- Premium/discount zone biasing of targets or eligible levels - there is no
  Fibonacci-retracement-zone filter in the current parameter set at all (an
  earlier `RequireFibRetracement` parameter this document used to reference
  has been removed); the only Fibonacci math left is the A-B-C-D
  extension/retracement arithmetic STRATEGY.md specifies for entry/stop/
  target, which is a different thing from a premium/discount gate.

## If either "still true" item is ever worth pursuing

1. Structure-based trend (HH/HL vs. LH/LL from the swing sequence) as an
   alternative or complement to the SMA filter - the more consistent fix,
   since it would use the same swing-detection machinery the strategy
   already runs, rather than a second, unrelated indicator family.
2. True CHoCH detection - would need a notion of "current structural bias"
   to define what counts as the *opposite* direction, which doesn't exist
   yet in a codebase whose only bias signal today is the SMA.

Either belongs to a genuine strategy extension, not a bug fix - confirm with
the user before building, since STRATEGY.md currently states the method is
complete as specified.
