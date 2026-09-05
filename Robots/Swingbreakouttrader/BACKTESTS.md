# Backtest log

## XAUUSD, M1, 08/09/2026 - 11/09/2026 (3 days)

Run via `ctrader-cli backtest` against the compiled `Swingbreakouttrader.algo`
(and its bundled `SwingBreakoutSFPSignal` indicator dependency), using every
parameter at its shipped default - i.e. the same defaults documented in the
class header and set from the XAUUSD M1 `.cbotset` (see commit history).
Raw report: [`backtests/XAUUSD-m1_2026-09-08_to_2026-09-11.json`](backtests/XAUUSD-m1_2026-09-08_to_2026-09-11.json).

**Command:**
```
ctrader-cli backtest Swingbreakouttrader.algo \
  --start=08/09/2026 --end=11/09/2026 \
  --data-mode=m1 --balance=200 \
  --account=<demo account> --symbol=XAUUSD --period=m1 \
  --report-json=XAUUSD-m1_2026-09-08_to_2026-09-11.json
```

**Result:**

| | |
|---|---|
| Starting balance | $200.00 |
| Ending balance | $246.22 |
| Net profit | +$46.22 |
| ROI | +23.11% |
| Total trades | 1 (1 short, 0 long) |
| Win rate | 100% (1/1) |
| Trade | Sell 0.01 lot XAUUSD, entry 4386.32 @ 2026-09-10 11:29 UTC, close 4340.10 @ 2026-09-10 12:35 UTC, net +$46.22 |
| Simulated spread / commission | 0 / 0 (backtest defaults - not overridden) |

**Caveats - read before treating this as validation:**

- **One trade is not a sample.** Three days on M1 with the default filters
  (session window, SMA trend filter, min/max risk bounds) produced exactly
  one signal that cleared every filter. This says the pipeline executes
  correctly end-to-end, not that the strategy has a demonstrated edge -
  that needs a backtest across many weeks/months and multiple symbols, per
  the class header's own disclaimer.
- **Zero spread and commission.** This run did not override `--spread` or
  `--commission`, so cTrader's backtest defaults (0/0) applied - the result
  is frictionless and better than live execution would be, especially on
  XAUUSD where real spread is a meaningful fraction of a typical stop.
- **M1 server bars, not tick data.** Run with `--data-mode=m1`, which
  replays 1-minute OHLC bars rather than historical ticks. This matches the
  strategy's own execution timeframe (see STRATEGY.md) but is a coarser
  simulation of intra-bar wick behavior than `--data-mode=ticks` would give
  for SFP sweep detection specifically.
- Always re-verify on a demo account with real spread/commission and a
  longer window before drawing any conclusion about live viability.
