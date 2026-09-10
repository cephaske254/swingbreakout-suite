using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    // =========================================================================
    // SwingBreakoutTrader - execution half of the SFP/B&R indicator/bot pair.
    // See STRATEGY.md for the full trading method (with diagrams) that this
    // pair implements.
    //
    // All signal detection (SFP and B&R level reactions across every
    // tracked level, the A-B-C-D fib-extension entry sequence, the SMA
    // trend-regime filter) lives in SwingBreakoutSFPSignal.cs (the
    // Indicator), which stays pure - no order or position logic. This bot
    // drives that indicator via Indicators.GetIndicator<SwingBreakoutSFPSignal>(...)
    // in OnStart() and only handles execution: position sizing, stop/target
    // placement, breakeven, trailing, and the entry-side filters that are
    // about TRADE MANAGEMENT rather than signal quality (spread, session,
    // clustering, opposite-direction blocking).
    //
    // *** PARAMETER ORDER IS LOAD-BEARING - READ THIS BEFORE EDITING ***
    // The 10 "Shared Signal Config" parameters below (Shared Signal Config
    // group, ending at MinSweepDepthATRmult) are forwarded to the indicator
    // POSITIONALLY via GetIndicator<T>(...) in OnStart() - they must appear
    // in the SAME ORDER as SwingBreakoutSFPSignal.cs declares its own
    // [Parameter] properties. If you add, remove, or reorder one of these,
    // update BOTH files' declarations AND the GetIndicator(...) call in the
    // same edit, or the two will silently go out of sync (wrong values get
    // passed to the wrong parameters - a subtle bug, not a build error).
    // Everything below that (Risk/Filters/Session groups) is execution-only
    // and NOT passed to the indicator - safe to add/reorder those freely.
    //
    // Each of these 10 parameters' DEFAULT VALUE comes from
    // SharedSignalDefaults.cs (in the indicator project) instead of being
    // hardcoded twice, so the two files can't drift on default values the
    // way they still can on order/names - change a default in one place.
    //
    // This parameter set is deliberately minimal - only knobs that are
    // genuinely independent decisions are exposed. Anything that only ever
    // mattered paired with another setting, or that risked the bot
    // "arguing with itself" through conflicting toggles, is either folded
    // into a single control or fixed as an internal constant near the top
    // of the class body.
    //
    // SETUP IN cTRADER AUTOMATE: build SwingBreakoutSFPSignal.cs as its own
    // Indicator algo FIRST, then create this Robot and add that indicator's
    // source file to the same project before building. See
    // SwingBreakoutSFPSignal.cs's header for the full setup notes and a
    // fallback plan if you hit a CT0003 "single algo type" build error.
    //
    // ENTRY: the only entry trigger is a signal from the indicator
    // (BullishSignal/BearishSignal on the just-closed bar) - fired when
    // price touches the 50% retracement of the D-C leg in the indicator's
    // A-B-C-D sequence (see STRATEGY.md). This Robot has no opinion on WHEN
    // within the indicator that bit gets set, it just acts on it.
    // OnBarClosed reads the signal and, if set, hands it straight to
    // TryEnter.
    //
    // TRADE MANAGEMENT:
    //   - Stop-loss and take-profit are both dictated by the strategy
    //     itself and used as-is, with NO buffer, floor, ceiling, or R:R
    //     hierarchy applied on top: StopAnchor is C (StopLossMode.
    //     Conservative) or A (StopLossMode.Normal), and TargetLevel is the
    //     261.8% extension of the C->D leg. See STRATEGY.md.
    //   - Min/Max risk amount (MinRiskAmount/MaxRiskAmount, account
    //     currency, 0 = off): estimates the trade's dollar risk from
    //     stopLossPips x Symbol.PipValue x volume and skips the trade if it
    //     falls outside the set range - filters out trades whose stop is so
    //     tight the risk is noise next to spread/commission, and caps how
    //     much a single unusually-wide-stop trade can risk.
    //   - Break-even (MoveToBreakeven, on by default): once open profit
    //     reaches BreakevenTriggerRR x the position's ORIGINAL risk (tracked
    //     per position, since the live stop no longer reflects it once
    //     moved), the stop jumps to entry plus a small fixed buffer
    //     (BreakevenBufferPips). Only ever tightens.
    //   - UseTrailingStop (off by default): once the same trigger is
    //     reached, the fixed TP is dropped and the stop trails
    //     TrailingStopATRmult x ATR behind price instead, ratcheting
    //     tighter every tick.
    //   - One position per direction at a time, and no flipping into the
    //     opposite direction while a position is open - both fixed
    //     internals (AllowMultiplePositions, BlockOppositeDirection; see
    //     below), not exposed as toggles.
    //
    // FILTERS (Filters group):
    //   - AvoidClusteredEntries: ignores a same-direction signal unless
    //     price has moved ClusterDistanceATRmult x ATR from the last
    //     same-direction entry - stops a ranging market producing a string
    //     of small losses at nearly the same level.
    //
    // TRADING SESSION (Session group): only acts on signals during the
    // configured window (07:00-20:00 UTC by default, covering the London
    // and New York sessions). TradeAllSessions removes the restriction.
    //
    // A handful of secondary knobs (whether multiple positions or
    // opposite-direction entries are ever allowed, breakeven buffer pips)
    // are fixed as constants near the top of the class body rather than
    // exposed, to keep the parameter panel small and every remaining knob
    // an independent, non-conflicting decision - edit them directly in code
    // if you want a different value.
    //
    // IMPORTANT
    //   - This automates order management; it is not literal "high-frequency
    //     trading" - normal broker-side latency, no colocation.
    //   - Always run this on a DEMO account and backtest across a meaningful
    //     date range and multiple symbols before ever pointing it at a live
    //     account. Nothing here is investment advice, and historical/
    //     backtested performance is not a guarantee of future results.
    //   - VolumeInLots is a fixed size, not computed from account equity -
    //     check it against your broker's minimum/step size and your own
    //     risk tolerance before running.
    // =========================================================================

    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class SwingBreakoutTrader : Robot
    {
        // === Shared Signal Config - forwarded to SwingBreakoutSFPSignal via ===
        // === GetIndicator<T>(...) in OnStart(). ORDER MATTERS - see above. ====
        [Parameter("Swing Lookback - Left Bars", DefaultValue = SharedSignalDefaults.SwingLeftBars, MinValue = 1, MaxValue = 50, Group = "Shared Signal Config")]
        public int SwingLeftBars { get; set; }

        [Parameter("Swing Lookback - Right Bars", DefaultValue = SharedSignalDefaults.SwingRightBars, MinValue = 1, MaxValue = 50, Group = "Shared Signal Config")]
        public int SwingRightBars { get; set; }

        [Parameter("Track Prior Swing High/Low", DefaultValue = SharedSignalDefaults.TrackPriorSwing, Group = "Shared Signal Config")]
        public bool TrackPriorSwing { get; set; }

        [Parameter("Track Prior Day High/Low/Close", DefaultValue = SharedSignalDefaults.TrackPDHPDL, Group = "Shared Signal Config")]
        public bool TrackPDHPDL { get; set; }

        [Parameter("Track Month-to-Date Close High/Low (HCOM/LCOM)", DefaultValue = SharedSignalDefaults.TrackHcomLcom, Group = "Shared Signal Config")]
        public bool TrackHcomLcom { get; set; }

        [Parameter("Higher Timeframe (for SMA trend filter)", DefaultValue = SharedSignalDefaults.TrendTimeFrame, Group = "Shared Signal Config")]
        public TimeFrame TrendTimeFrame { get; set; }

        [Parameter("Require SMA trend filter (direction + separation)", DefaultValue = SharedSignalDefaults.RequireTrendFilter, Group = "Shared Signal Config")]
        public bool RequireTrendFilter { get; set; }

        [Parameter("ATR Period", DefaultValue = SharedSignalDefaults.AtrPeriod, MinValue = 1, Group = "Shared Signal Config")]
        public int AtrPeriod { get; set; }

        [Parameter("Min. Sweep/Break Depth beyond level (x ATR)", DefaultValue = SharedSignalDefaults.MinSweepDepthATRmult, MinValue = 0.0, Step = 0.05, Group = "Shared Signal Config")]
        public double MinSweepDepthATRmult { get; set; }

        [Parameter("Stop-Loss Mode", DefaultValue = SharedSignalDefaults.StopMode, Group = "Shared Signal Config")]
        public StopLossMode StopMode { get; set; }
        // === End of shared config - everything below is execution-only. ======

        // === Master switch =======================================================
        // Total override: while OFF, TryEnter refuses every signal and no new
        // position is ever opened, regardless of what any other parameter
        // says. Existing open positions are still managed (breakeven/
        // trailing) - this only blocks NEW entries, so flipping it off never
        // strands an open trade with no stop management.
        [Parameter("Enable Trading (OFF = no new entries, total override)", DefaultValue = true, Group = "Master Switch")]
        public bool EnableTrading { get; set; }

        // === Risk / trade management ==========================================
        // Stop-loss and take-profit are both dictated by the strategy itself
        // (the indicator's StopAnchor = A or C per StopMode, and TargetLevel
        // = the 261.8% C->D extension) - no ATR buffer, floor, ceiling, or
        // R:R hierarchy is applied on top. See STRATEGY.md.
        [Parameter("Volume (lots)", DefaultValue = 0.01, MinValue = 0.01, Step = 0.01, Group = "Risk")]
        public double VolumeInLots { get; set; }

        [Parameter("Min. risk per trade, account currency (0 = off)", DefaultValue = 6.0, MinValue = 0.0, Step = 0.5, Group = "Risk")]
        public double MinRiskAmount { get; set; }

        [Parameter("Max. risk per trade, account currency (0 = off)", DefaultValue = 15.0, MinValue = 0.0, Step = 1.0, Group = "Risk")]
        public double MaxRiskAmount { get; set; }

        // Defaulted for XAUUSD as the primary/tested instrument. If running
        // this on a tighter-spread symbol (e.g. NAS100), lower this to
        // roughly 15-20 manually - one shared default can't auto-adjust
        // per instrument.
        [Parameter("Max spread to trade (pips, 0 = no limit)", DefaultValue = 0.0, MinValue = 0.0, Step = 0.5, Group = "Risk")]
        public double MaxSpreadPips { get; set; }

        [Parameter("Move stop to breakeven", DefaultValue = true, Group = "Risk")]
        public bool MoveToBreakeven { get; set; }

        [Parameter("Breakeven Trigger (x original risk / R)", DefaultValue = 0.5, MinValue = 0.1, Step = 0.1, Group = "Risk")]
        public double BreakevenTriggerRR { get; set; }

        [Parameter("Use trailing stop (replaces the fixed TP once active)", DefaultValue = false, Group = "Risk")]
        public bool UseTrailingStop { get; set; }

        [Parameter("Trailing Stop (x ATR)", DefaultValue = 5.0, MinValue = 0.1, Step = 0.1, Group = "Risk")]
        public double TrailingStopATRmult { get; set; }

        // === Filters (clustering / direction conflict) ========================
        [Parameter("Avoid clustered re-entries (same direction, same area)", DefaultValue = false, Group = "Filters")]
        public bool AvoidClusteredEntries { get; set; }

        [Parameter("Min. distance from last same-direction entry (x ATR)", DefaultValue = 1.5, MinValue = 0.0, Step = 0.1, Group = "Filters")]
        public double ClusterDistanceATRmult { get; set; }

        // === Trading session ====================================================
        [Parameter("Trade all sessions (ignore the window below)", DefaultValue = true, Group = "Session")]
        public bool TradeAllSessions { get; set; }

        [Parameter("Session Start Hour (UTC)", DefaultValue = 7, MinValue = 0, MaxValue = 23, Group = "Session")]
        public int SessionStartHourUTC { get; set; }

        [Parameter("Session End Hour (UTC)", DefaultValue = 20, MinValue = 0, MaxValue = 23, Group = "Session")]
        public int SessionEndHourUTC { get; set; }

        // === Fixed internals =====================================================
        // One position per direction at a time, and no flipping into the
        // opposite direction while a position is open - basic guardrails
        // that don't need to be user-configurable.
        private const bool AllowMultiplePositions = false;
        private const bool BlockOppositeDirection = true;
        private const double BreakevenBufferPips = 1.0;

        private const string Label = "SFPTrader";
        private const string TradingStatusChartObjectName = "SFPTrader_TradingStatus";

        private SwingBreakoutSFPSignal _signal;
        private AverageTrueRange _atr;

        private int _lastProcessedIndex = -1;

        // Entry price of the last trade taken in each direction - used by
        // AvoidClusteredEntries to require a minimum distance before another
        // same-direction entry is allowed.
        private double _lastLongEntryPrice = double.NaN;
        private double _lastShortEntryPrice = double.NaN;

        // Original stop distance (entry to initial stop, in price units) per
        // open position - needed because once the stop is moved to breakeven,
        // position.StopLoss no longer reflects the original risk, so "1R
        // profit reached" can't be derived from the live stop alone.
        private readonly Dictionary<long, double> _originalRiskDistance = new Dictionary<long, double>();

        protected override void OnStart()
        {
            // Forwarding the shared parameters into the indicator - ORDER
            // MUST MATCH SwingBreakoutSFPSignal.cs's own [Parameter]
            // declaration order exactly. See the class header.
            _signal = Indicators.GetIndicator<SwingBreakoutSFPSignal>(
                SwingLeftBars, SwingRightBars,
                TrackPriorSwing, TrackPDHPDL, TrackHcomLcom, TrendTimeFrame,
                RequireTrendFilter,
                AtrPeriod, MinSweepDepthATRmult, StopMode);

            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.WilderSmoothing);
            Positions.Closed += OnPositionClosed;

            UpdateTradingStatusDisplay();
        }

        private void OnPositionClosed(PositionClosedEventArgs args)
        {
            _originalRiskDistance.Remove(args.Position.Id);
        }

        // Static (chart-anchored, not price/time-anchored) text in the
        // top-right corner: a colored dot + ON/OFF label reflecting
        // EnableTrading. Drawn once at start - EnableTrading is a startup
        // parameter and can't change while the bot is running, so nothing
        // else needs to trigger a redraw.
        private void UpdateTradingStatusDisplay()
        {
            var color = EnableTrading ? Color.LimeGreen : Color.Red;
            var label = EnableTrading ? "Trading: ON" : "Trading: OFF";
            Chart.DrawStaticText(TradingStatusChartObjectName, $"● {label}", VerticalAlignment.Top, HorizontalAlignment.Right, color);
        }

        protected override void OnBarClosed()
        {
            int index = Bars.Count - 2;
            if (index < 0 || index <= _lastProcessedIndex)
                return;

            if (_signal.BearishSignal[index] > 0.5)
                TryEnter(-1, index, _signal.StopAnchor[index], _signal.TargetLevel[index]);
            if (_signal.BullishSignal[index] > 0.5)
                TryEnter(1, index, _signal.StopAnchor[index], _signal.TargetLevel[index]);

            _lastProcessedIndex = index;
        }

        protected override void OnTick()
        {
            if (MoveToBreakeven || UseTrailingStop)
                ManageOpenPositions();
        }

        // Moves the stop to breakeven (+ a small fixed buffer) and/or trails
        // it behind price once open profit reaches BreakevenTriggerRR x the
        // position's ORIGINAL risk distance. Only ever tightens toward
        // price - never re-loosens an already-moved stop, and does nothing
        // once the position isn't tracked (e.g. the bot was restarted after
        // the position opened, so its original risk is unknown).
        //
        // MoveToBreakeven and UseTrailingStop are independent toggles that
        // both key off the same trigger distance:
        //   - MoveToBreakeven only: stop jumps to entry + buffer and stays
        //     there.
        //   - UseTrailingStop only: no breakeven jump, but once triggered the
        //     fixed take-profit is dropped and the stop trails ATR-distance
        //     behind price instead.
        //   - Both on: the stop is whichever is more favorable of the
        //     breakeven level and the ATR trail, and the fixed TP is dropped
        //     as soon as trailing engages.
        private void ManageOpenPositions()
        {
            double bufferPrice = BreakevenBufferPips * Symbol.PipSize;
            double atrValue = _atr.Result.LastValue;
            bool trailingActive = UseTrailingStop && !double.IsNaN(atrValue) && atrValue > 0;

            foreach (var position in GetMyPositions())
            {
                if (!_originalRiskDistance.TryGetValue(position.Id, out double riskDistance))
                    continue;

                double triggerDistance = riskDistance * BreakevenTriggerRR;

                double profitDistance = position.TradeType == TradeType.Buy
                    ? Symbol.Bid - position.EntryPrice
                    : position.EntryPrice - Symbol.Ask;
                if (profitDistance < triggerDistance) continue;

                double? candidate = null;

                if (position.TradeType == TradeType.Buy)
                {
                    if (MoveToBreakeven)
                        candidate = position.EntryPrice + bufferPrice;
                    if (trailingActive)
                    {
                        double trailLevel = Symbol.Bid - atrValue * TrailingStopATRmult;
                        candidate = candidate.HasValue ? Math.Max(candidate.Value, trailLevel) : trailLevel;
                    }
                }
                else
                {
                    if (MoveToBreakeven)
                        candidate = position.EntryPrice - bufferPrice;
                    if (trailingActive)
                    {
                        double trailLevel = Symbol.Ask + atrValue * TrailingStopATRmult;
                        candidate = candidate.HasValue ? Math.Min(candidate.Value, trailLevel) : trailLevel;
                    }
                }

                if (!candidate.HasValue)
                    continue;

                bool slImproves = position.StopLoss == null || (position.TradeType == TradeType.Buy
                    ? candidate.Value > position.StopLoss.Value
                    : candidate.Value < position.StopLoss.Value);

                bool tpNeedsClearing = trailingActive && position.TakeProfit.HasValue;
                if (!slImproves && !tpNeedsClearing)
                    continue;

                double slToSet = slImproves ? candidate.Value : position.StopLoss.Value;
                double? tpToSet = trailingActive ? (double?)null : position.TakeProfit;
                ModifyPosition(position, slToSet, tpToSet);
            }
        }

        // stopLevel and targetLevel come straight from the indicator's
        // StopAnchor/TargetLevel outputs - the strategy (STRATEGY.md) fully
        // specifies both, so they're used as-is, with no ATR buffer, floor,
        // ceiling, or R:R hierarchy layered on top. This method just applies
        // the trade-management filters (spread, session, direction conflict,
        // clustering, risk-amount bounds) before firing the order.
        private void TryEnter(int dir, int index, double stopLevel, double targetLevel)
        {
            if (!EnableTrading)
                return;

            if (double.IsNaN(stopLevel) || double.IsNaN(targetLevel))
                return;

            if (SpreadTooWide())
                return;

            if (!WithinTradingSession(Bars.OpenTimes[index]))
                return;

            var tradeType = dir == 1 ? TradeType.Buy : TradeType.Sell;
            var myPositions = GetMyPositions();
            bool alreadySameDirection = myPositions.Exists(p => p.TradeType == tradeType);
            if (!AllowMultiplePositions && alreadySameDirection)
                return;

            // Don't sell into an open buy, or buy into an open sell. An
            // opposite-direction signal while a position is still open is
            // treated as noise against a thesis already proven right, not as
            // an instruction to flip - the open position has to close first.
            if (BlockOppositeDirection)
            {
                bool oppositeOpen = myPositions.Exists(p => p.TradeType != tradeType);
                if (oppositeOpen)
                    return;
            }

            double price = Bars.ClosePrices[index];

            // Don't re-enter the same direction right on top of the last
            // entry in that direction - that's what turns a ranging market
            // into a string of small losses at nearly the same level instead
            // of one clean trade. Require price to have moved at least
            // ClusterDistanceATRmult x ATR away from the last same-direction
            // entry before allowing another one.
            if (AvoidClusteredEntries)
            {
                double atrVal = _atr.Result[index];
                double lastEntry = dir == 1 ? _lastLongEntryPrice : _lastShortEntryPrice;
                if (!double.IsNaN(lastEntry) && !double.IsNaN(atrVal) && atrVal > 0)
                {
                    double distanceFromLastEntry = Math.Abs(price - lastEntry);
                    if (distanceFromLastEntry < ClusterDistanceATRmult * atrVal)
                        return;
                }
            }

            double stopDistance = Math.Abs(price - stopLevel);
            if (stopDistance <= 0)
                return;

            bool targetOnCorrectSide = dir == 1 ? targetLevel > price : targetLevel < price;
            if (!targetOnCorrectSide)
                return;
            double takeProfitDistance = Math.Abs(targetLevel - price);

            double stopLossPips = stopDistance / Symbol.PipSize;
            double takeProfitPips = takeProfitDistance / Symbol.PipSize;

            double volume = Symbol.QuantityToVolumeInUnits(VolumeInLots);

            // Estimated dollar risk on this trade at the fixed VolumeInLots
            // size: stopLossPips x Symbol.PipValue (cAlgo's pip value per
            // single unit of volume, in account currency) x volume in units
            // - cAlgo's own standard formula for pre-trade risk estimation,
            // solved for risk instead of for volume. Skips trades whose
            // stop is so tight the risk is basically noise next to spread/
            // commission, and/or caps how much any single trade can risk if
            // MaxRiskAmount is set.
            double estimatedRiskAmount = stopLossPips * Symbol.PipValue * volume;
            if (MinRiskAmount > 0 && estimatedRiskAmount < MinRiskAmount)
                return;
            if (MaxRiskAmount > 0 && estimatedRiskAmount > MaxRiskAmount)
                return;

            var result = ExecuteMarketOrder(tradeType, SymbolName, volume, Label, stopLossPips, takeProfitPips);
            if (result.IsSuccessful && result.Position != null)
            {
                _originalRiskDistance[result.Position.Id] = stopDistance;
                if (dir == 1)
                    _lastLongEntryPrice = result.Position.EntryPrice;
                else
                    _lastShortEntryPrice = result.Position.EntryPrice;
            }
        }

        private bool SpreadTooWide()
        {
            if (MaxSpreadPips <= 0) return false;
            double spreadPips = (Symbol.Ask - Symbol.Bid) / Symbol.PipSize;
            return spreadPips > MaxSpreadPips;
        }

        // London (~08:00-17:00 UTC) and New York (~13:00-22:00 UTC) overlap,
        // so together they form one continuous window rather than two
        // separate ones - hence a single Start/End pair rather than four
        // parameters. Actual session hours shift with London/NY daylight
        // saving (which don't change on the same dates), so the fixed
        // default is an approximation - narrow or widen the window if you
        // want it precise for a given time of year.
        private bool WithinTradingSession(DateTime barTime)
        {
            if (TradeAllSessions)
                return true;

            int hour = barTime.Hour;
            if (SessionStartHourUTC <= SessionEndHourUTC)
                return hour >= SessionStartHourUTC && hour < SessionEndHourUTC;

            return hour >= SessionStartHourUTC || hour < SessionEndHourUTC;
        }

        private List<Position> GetMyPositions()
        {
            var result = new List<Position>();
            foreach (var p in Positions)
                if (p.Label == Label && p.SymbolName == SymbolName)
                    result.Add(p);
            return result;
        }
    }
}