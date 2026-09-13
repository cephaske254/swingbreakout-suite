# Our Backtest Log

We run every test with `ctrader-cli backtest` against the compiled `Swingbreakouttrader.algo`
(bundling its `SwingBreakoutSFPSignal` indicator dependency), XAUUSD, M1,
every parameter at its shipped default, $200 starting balance, on a demo
account.

**We changed the defaults on 2026-09-11.** We updated the tested XAUUSD M1 `.cbotset`
(`TrendTimeFrame` m5→m1, `MinRiskAmount` 5→6, `BreakevenTriggerRR`
0.25→0.5, `TradeAllSessions` false→true, plus the new `EnableTrading`
parameter defaulting true) and the shipped defaults were re-synced to match.
**Runs 1-9 below all predate that change** and were run under the prior
default set (session window restricted to 07:00-20:00 UTC, tighter
breakeven trigger, no `EnableTrading` parameter yet) - they are not directly
comparable to "Run 1b" onward, which reflects the current defaults. Each
run's own section says which default set it used.

## Summary

| Window | Length | Trades | Net | ROI | Defaults | Note |
|---|---|---|---|---|---|---|
| 08/09 - 11/09/2026 | 3d | 1 | +$46.22 | +23.11% | prior | original run |
| 31/08 - 03/09/2026 | 3d | 0 | $0.00 | 0% | prior | |
| 01/09 - 04/09/2026 | 3d | 1 | -$8.15 | -4.08% | prior | see boundary anomaly below |
| 02/09 - 05/09/2026 | 3d | 1 | -$8.15 | -4.08% | prior | same trade as above |
| 03/09 - 06/09/2026 | 3d | 1 | -$8.15 | -4.08% | prior | same trade as above |
| 04/09 - 07/09/2026 | 3d | 0 | $0.00 | 0% | prior | |
| 15/06 - 22/06/2026 | 7d (full week) | 1 | -$11.08 | -5.54% | prior | June |
| 27/07 - 03/08/2026 | 7d (full week) | 0 | $0.00 | 0% | prior | July |
| 03/08 - 10/08/2026 | 7d (full week) | 0 | $0.00 | 0% | prior | August |
| 08/09 - 11/09/2026 (redo) | 3d | 2 | +$46.22 | +23.11% | **current** | Run 1b, see below |

**We do not sum these as six independent samples.** Three of the five
randomized windows (01-04/09, 02-05/09, 03-06/09) overlap and captured the
exact same physical trade - see "Overlap and an observed anomaly" below.
Treated correctly, this is really only 4 distinct trade outcomes (1 win,
1 loss, seen 3 times across overlapping windows, plus 2 flat windows) across
6 backtest windows, not 6 independent trials - nowhere near enough for any
statistical conclusion, consistent with the single-trade caveat from the
first run.

## Run 1: 08/09/2026 - 11/09/2026 (3 days) - prior defaults

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

## Run 1b: 08/09/2026 - 11/09/2026 (3 days) - redo with current defaults

Same window as Run 1, re-run after the 2026-09-11 default sync (see the
note at the top of this file). Key differences from Run 1's defaults:
`TradeAllSessions=true` (was `false`, session window no longer restricts
entries to 07:00-20:00 UTC), `BreakevenTriggerRR=0.5` (was `0.25`),
`MinRiskAmount=6` (was `5`), `TrendTimeFrame=Minute` (was `Minute5`), plus
the new `EnableTrading=true` parameter (no behavior change at its default).

Raw report: [`backtests/XAUUSD-m1_2026-09-08_to_2026-09-11_v2.json`](backtests/XAUUSD-m1_2026-09-08_to_2026-09-11_v2.json).

**Command:** identical to Run 1's, just against the rebuilt `.algo`.

**Result:**

| | |
|---|---|
| Starting balance | $200.00 |
| Ending balance | $246.22 |
| Net profit | +$46.22 |
| ROI | +23.11% |
| Total trades | 2 (1 short, 1 long) |
| Win rate | 100% (2/2) - the second trade closed flat, not a real profit |
| Trade 1 | Sell 0.01 lot, entry 4386.32 @ 2026-09-10 11:29 UTC, close 4340.10 @ 12:35 UTC, net +$46.22 - identical to Run 1's only trade |
| Trade 2 | Buy 0.01 lot, entry 4324.89 @ 2026-09-11 01:24 UTC, close 4324.89 @ 01:41 UTC, net $0.00 |
| Simulated spread / commission | 0 / 0 (backtest defaults - not overridden) |

Trade 2 only exists because of the defaults change: it entered at 01:24 UTC,
outside the old 07:00-20:00 UTC session window, so `TradeAllSessions=true`
is what let it through. It closed at breakeven almost immediately - most
likely the `BreakevenTriggerRR=0.5` stop-move firing and then price
reversing right back through the moved stop, which reads as a demonstration
that the wider `TradeAllSessions` setting can admit low-quality, out-of-hours
signals that a session filter would otherwise have screened out - net
contribution here is exactly $0, not a genuine second win, despite showing
as "winning" in the trade-count field.

## Runs 2-6: five randomized 3-day windows within one week

**Methodology:** We picked one calendar week, Monday 31/08/2026 through
Sunday 06/09/2026. A 3-consecutive-calendar-day window fully inside a 7-day
week has exactly 5 possible start days (Mon-Fri); we produced the run order
below with `random.shuffle` over those 5 offsets (see commit history for the
exact script), rather than hand-picking it. Each window uses the same command
template as Run 1; only `--start`, `--end`, and `--report-json` change:

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
model. We did not investigate this further. The root cause is unconfirmed: it may
be how `ctrader-cli backtest` interprets `--end`, how much historical
warm-up data a given `--start` makes available to the indicator's swing/
level tracking, or something else. We flag it rather than present the
01/09-04/09 and 04/09-07/09 results as more precise than they are. We treat
trade timing at a backtest window's edges with caution; the 02/09-05/09 and
03/09-06/09 results, where the trade sits comfortably inside the window, are
less exposed to this question.

## Runs 7-9: one full week per month, three different months

**Methodology:** We selected three different months (June, July, and August
2026, all complete when we ran these tests, so no future or unavailable data)
and randomly chose one week in each month (`random.choice` over that month's
Mondays). We backtested the **whole week** (Monday 00:00 UTC to the following
Monday 00:00 UTC) rather than a 3-day slice. A full week gives the strategy's
session-window and clustering filters more room to matter. We use the same
command template as the runs above; only the window length changes:

```
ctrader-cli backtest Swingbreakouttrader.algo \
  --start=<dd/MM/2026> --end=<dd/MM/2026> \
  --data-mode=m1 --balance=200 \
  --account=<demo account> --symbol=XAUUSD --period=m1 \
  --report-json=<path>
```

Raw reports:
[`June: 15/06-22/06`](backtests/XAUUSD-m1_week_2026-06-15_to_2026-06-22.json) ·
[`July: 27/07-03/08`](backtests/XAUUSD-m1_week_2026-07-27_to_2026-08-03.json) ·
[`August: 03/08-10/08`](backtests/XAUUSD-m1_week_2026-08-03_to_2026-08-10.json)

| Month | Window | Trades | Net | ROI | Trade detail |
|---|---|---|---|---|---|
| June | 15/06 - 22/06 | 1 | -$11.08 | -5.54% | Sell 0.01 lot, entry 4322.25 @ 2026-06-17 08:53 UTC, close 4333.33 @ 09:27 UTC |
| July | 27/07 - 03/08 | 0 | $0.00 | 0% | - |
| August | 03/08 - 10/08 | 0 | $0.00 | 0% | - |

Two of the three full-week windows produced zero trades, and the one that
did was a loss - across all 9 runs logged in this file (6 shorter windows +
these 3 full weeks), the strategy has now produced 5 losing/flat outcomes
against 1 win. That is a small, informal sample across scattered dates, not
a rigorous walk-forward test (no fixed cadence, no out-of-sample split), but
it's worth being direct about: nothing here yet supports the strategy having
a demonstrated edge, and the one clearly profitable run (the original,
08/09-11/09) looks more like an outlier than a baseline once more weeks are
added.

## Caveats we apply to every run above

- **Tiny sample size.** Nine backtest windows (several 3-day windows
  overlapping) produced 6 distinct trade outcomes total, spanning three
  months. This demonstrates the pipeline executes correctly end-to-end
  across different weeks/dates, not that the strategy has a demonstrated
  edge - that needs a backtest across many weeks/months
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
- We always re-verify on a demo account with real spread/commission and a
  longer, non-overlapping window before drawing conclusions about live
  viability.
