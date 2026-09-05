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

## Testing status

Tested on **XAUUSD** with the robot's **default parameters** (matching the
tested XAUUSD M1 `.cbotset`). The most recent run is a 3-day M1 backtest
(08/09/2026-11/09/2026) via `ctrader-cli backtest`: +23.11% ROI on a $200
starting balance across a single trade. See
[`Robots/Swingbreakouttrader/BACKTESTS.md`](Robots/Swingbreakouttrader/BACKTESTS.md)
for the full write-up, method, and caveats (single-trade sample size, zero
simulated spread/commission, M1 bar replay) — read those caveats before
treating this as validation of an edge. The raw report JSON is attached at
[`Robots/Swingbreakouttrader/backtests/XAUUSD-m1_2026-09-08_to_2026-09-11.json`](Robots/Swingbreakouttrader/backtests/XAUUSD-m1_2026-09-08_to_2026-09-11.json).

## Disclaimer

Always run on a demo account and backtest across a meaningful date range
and multiple symbols before pointing this at a live account. Nothing here
is investment advice, and historical/backtested performance is not a
guarantee of future results.
