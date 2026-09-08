# SwingBreakout SFP/B&R Suite

Two cTrader/cAlgo algos implementing an SFP (Swing Failure Pattern) / B&R
(Break & Retest) trading method with an A-B-C-D fib-extension entry
sequence, split into a pure signal indicator and an execution robot:

- **`Indicators/SwingBreakoutSFPSignal/`** — the indicator. Detects SFP and
  B&R reactions at PDH/PDL/PDC, month-to-date close highs/lows, and swing
  highs/lows; runs the A-B-C-D entry sequence; applies an SMA trend-regime
  filter. No order or position logic. See `STRATEGY.md` for the full
  method with diagrams.
- **`Robots/Swingbreakouttrader/`** — the execution robot. Drives the
  indicator via `Indicators.GetIndicator<SwingBreakoutSFPSignal>(...)` and
  handles only trade management: sizing, stop/target placement (taken
  as-is from the indicator), breakeven, trailing stop, and entry-side
  filters (spread, session, clustering, opposite-direction blocking).

## Build

Each project is a standalone `net6.0` class library targeting the
`cTrader.Automate` package. The robot's `.csproj` has a `ProjectReference`
to the indicator's `.csproj`, so building the robot also builds the
indicator:

```
cd Robots/Swingbreakouttrader/Swingbreakouttrader
dotnet build
```

To build inside cTrader Automate itself (rather than via `dotnet build`),
create the indicator algo first, then create the robot algo and add the
indicator's `.cs` files (`SwingBreakoutSFPSignal.cs` and
`SharedSignalDefaults.cs`) to the same robot project before building — see
the header comments in `Swingbreakouttrader.cs` for the full setup notes.

## Parameter defaults

The 10 parameters shared between both algos are forwarded **positionally**
from the robot to the indicator via `GetIndicator<T>(...)` — see the
header comment in `Swingbreakouttrader.cs` before reordering any of them.
Their default values live in one place,
`Indicators/SwingBreakoutSFPSignal/SwingBreakoutSFPSignal/SharedSignalDefaults.cs`,
referenced by both files' `[Parameter]` attributes.

## Strategy diagrams

![SFP detection diagram](sfp-detection-diagram.svg)

![B&R detection diagram](bnr-detection-diagram.svg)

![Entry sequence diagram: A/B/C/D, the 100% extension, and the 50% D→C entry](strategy-entry-diagram.svg)

See [`Indicators/SwingBreakoutSFPSignal/STRATEGY.md`](Indicators/SwingBreakoutSFPSignal/STRATEGY.md) for the full write-up these diagrams belong to.

## Testing status

Tested on **XAUUSD** with the robot's **default parameters** (matching the
tested XAUUSD M1 `.cbotset`), via `ctrader-cli backtest`, M1, $200 starting
balance: one 3-day run (08/09/2026-11/09/2026, +23.11% ROI, 1 trade) plus
five more 3-day windows randomized within a single week (31/08-06/09/2026).
See [`Robots/Swingbreakouttrader/BACKTESTS.md`](Robots/Swingbreakouttrader/BACKTESTS.md)
for the full write-up, method, and caveats — including three overlapping
windows that captured the same trade (not independent samples) and an
observed backtest-boundary anomaly — read those caveats before treating any
of this as validation of an edge. Raw report JSON for all six runs is
attached under
[`Robots/Swingbreakouttrader/backtests/`](Robots/Swingbreakouttrader/backtests/).

## Disclaimer

Always run on a demo account and backtest across a meaningful date range
and multiple symbols before pointing this at a live account. Nothing here
is investment advice, and historical/backtested performance is not a
guarantee of future results.
