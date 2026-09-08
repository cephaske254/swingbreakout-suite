# Backtest log

All runs use `ctrader-cli backtest` against the compiled `Swingbreakouttrader.algo`
(bundling its `SwingBreakoutSFPSignal` indicator dependency), XAUUSD, M1,
every parameter at its shipped default (matching the tested XAUUSD M1
`.cbotset`), $200 starting balance, on a demo account.

## Summary

| Window | Start weekday | Trades | Net | ROI | Note |
|---|---|---|---|---|---|
| 08/09 - 11/09/2026 | Tue | 1 | +$46.22 | +23.11% | original run |
| 31/08 - 03/09/2026 | Mon | 0 | $0.00 | 0% | |
| 01/09 - 04/09/2026 | Tue | 1 | -$8.15 | -4.08% | see boundary anomaly below |
| 02/09 - 05/09/2026 | Wed | 1 | -$8.15 | -4.08% | same trade as above |
| 03/09 - 06/09/2026 | Thu | 1 | -$8.15 | -4.08% | same trade as above |
| 04/09 - 07/09/2026 | Fri | 0 | $0.00 | 0% | |

**Do not sum these as six independent samples.** Three of the five
randomized windows (01-04/09, 02-05/09, 03-06/09) overlap and captured the
exact same physical trade - see "Overlap and an observed anomaly" below.
Treated correctly, this is really only 4 distinct trade outcomes (1 win,
1 loss, seen 3 times across overlapping windows, plus 2 flat windows) across
6 backtest windows, not 6 independent trials - nowhere near enough for any
statistical conclusion, consistent with the single-trade caveat from the
first run.

## Run 1: 08/09/2026 - 11/09/2026 (3 days)

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

## Runs 2-6: five randomized 3-day windows within one week

**Methodology:** picked one calendar week, Monday 31/08/2026 through Sunday
06/09/2026. A 3-consecutive-calendar-day window fully inside a 7-day week
has exactly 5 possible start days (Mon-Fri); the run order below was
produced by `random.shuffle` over those 5 offsets (see commit history for
the exact script) rather than hand-picked. Each window uses the identical
command template as Run 1, only `--start`/`--end`/`--report-json` change:

```
ctrader-cli backtest Swingbreakouttrader.algo \
  --start=<dd/MM/2026> --end=<dd/MM/2026> \
  --data-mode=m1 --balance=200 \
  --account=<demo account> --symbol=XAUUSD --period=m1 \
  --report-json=<path>
```

Raw reports:
[`31/08-03/09`](backtests/XAUUSD-m1_2026-08-31_to_2026-09-03.json) ·
[`01/09-04/09`](backtests/XAUUSD-m1_2026-09-01_to_2026-09-04.json) ·
[`02/09-05/09`](backtests/XAUUSD-m1_2026-09-02_to_2026-09-05.json) ·
[`03/09-06/09`](backtests/XAUUSD-m1_2026-09-03_to_2026-09-06.json) ·
[`04/09-07/09`](backtests/XAUUSD-m1_2026-09-04_to_2026-09-07.json)

| Window | Trades | Net | ROI | Trade detail |
|---|---|---|---|---|
| 31/08 - 03/09 | 0 | $0.00 | 0% | - |
| 01/09 - 04/09 | 1 | -$8.15 | -4.08% | Sell 0.01 lot, entry 4466.22 @ 2026-09-04 07:06 UTC, close 4474.37 @ 07:31 UTC |
| 02/09 - 05/09 | 1 | -$8.15 | -4.08% | same trade (entry/close identical to above) |
| 03/09 - 06/09 | 1 | -$8.15 | -4.08% | same trade (entry/close identical to above) |
| 04/09 - 07/09 | 0 | $0.00 | 0% | - |

### Overlap and an observed anomaly

The 01-04/09, 02-05/09, and 03-06/09 windows all overlap (each includes
2026-09-04), and all three reported the identical trade - same entry price,
close price, and timestamps, byte-identical in the raw JSON. That's expected
for genuinely overlapping windows and is *not* three separate losses.

What's worth flagging: the 01/09-04/09 window's nominal end boundary is
2026-09-04T00:00:00Z, yet its one reported trade entered at
2026-09-04T07:06:00Z - **after** that boundary. Conversely, the 04/09-07/09
window starts exactly at 2026-09-04T00:00:00Z and should, on a naive reading,
also cover that same 07:06 UTC entry, but it reported zero trades. Both
windows can't be simultaneously "correct" under a simple start/end boundary
model. This wasn't investigated further (root cause is unconfirmed - could
be how `ctrader-cli backtest` interprets `--end`, how much historical
warm-up data a given `--start` makes available to the indicator's swing/
level tracking, or something else) - flagging it here rather than
presenting the 01/09-04/09 and 04/09-07/09 results as more precise than they
are. Treat exact trade timing right at a backtest window's edges with
caution; the 02/09-05/09 and 03/09-06/09 results, where the trade sits
comfortably inside the window rather than at its boundary, are less exposed
to this specific question.

## Caveats that apply to every run above

- **Tiny sample size.** Six 3-day windows (several overlapping) produced 4
  distinct trade outcomes total. This demonstrates the pipeline executes
  correctly end-to-end across different weeks/dates, not that the strategy
  has a demonstrated edge - that needs a backtest across many weeks/months
  and multiple symbols, per the class header's own disclaimer.
- **Zero spread and commission.** None of these runs overrode `--spread` or
  `--commission`, so cTrader's backtest defaults (0/0) applied throughout -
  every result above is frictionless and better than live execution would
  be, especially on XAUUSD where real spread is a meaningful fraction of a
  typical stop.
- **M1 server bars, not tick data.** All runs used `--data-mode=m1`, which
  replays 1-minute OHLC bars rather than historical ticks. This matches the
  strategy's own execution timeframe (see STRATEGY.md) but is a coarser
  simulation of intra-bar wick behavior than `--data-mode=ticks` would give
  for SFP sweep detection specifically.
- Always re-verify on a demo account with real spread/commission and a
  longer, non-overlapping window before drawing any conclusion about live
  viability.
