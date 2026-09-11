using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    // =========================================================================
    // SwingBreakoutSFPSignal - level-reaction (SFP/B&R) + A-B-C-D fib-extension
    // entry signal indicator. See STRATEGY.md for the full trading method this
    // implements, in the user's own terms, with diagrams.
    //
    // Pure signal detection only - no order or position logic lives here.
    // SwingBreakoutTrader.cs (the Robot) drives this indicator via
    // Indicators.GetIndicator<SwingBreakoutSFPSignal>(...) in its OnStart(),
    // forwarding its own [Parameter] values in, so the bot's parameter panel
    // is the single place that configures both algos.
    //
    // *** PARAMETER ORDER IS LOAD-BEARING ***
    // GetIndicator<T>(args) maps args to this class's [Parameter] properties
    // POSITIONALLY, in the exact order declared below - NOT by name. The
    // Robot's OnStart() call must pass arguments in this SAME order. If you
    // add, remove, or reorder a [Parameter] here, update the
    // GetIndicator<SwingBreakoutSFPSignal>(...) call in SwingBreakoutTrader.cs
    // to match, in the same edit - they are two files that must move
    // together.
    //
    // WHAT THIS DETECTS (see STRATEGY.md for diagrams of every step)
    //
    // STEP 1 - Level reaction. Every enabled level type (current-timeframe
    // swing high/low, PDH/PDL/PDC, HCOM/LCOM) is watched for one of two
    // outcomes:
    //   - SFP (Swing Failure Pattern): a single bar's wick sweeps at least
    //     MinSweepDepthATRmult x ATR past the level and closes back on the
    //     other side, in the SAME bar.
    //   - B&R (Break & Retest): a bar CLOSES beyond the level by the same
    //     threshold (a real break); from then on the break is "pending"
    //     indefinitely (no fixed bar window) until either a later bar wicks
    //     back to touch the broken level while still closing beyond it (B&R
    //     CONFIRMED on that bar), or a bar closes back through the level in
    //     the failure direction first (break invalidated, nothing fires).
    // Either outcome is a "trigger" that establishes direction and hands off
    // a starting point (A) to step 2.
    //
    // STEP 2 - A-B-C-D fib sequence, run independently per direction:
    //   A = the trigger bar's extreme (low for bullish, high for bearish).
    //   B = the next confirmed swing pivot beyond A (first leg).
    //   C = the next confirmed swing pivot back beyond B (first retracement).
    //   Wait for a close past the 100% extension of A-B-C (= C + (B-A)).
    //   D = the next confirmed swing pivot after that break (the new
    //       extreme of the second leg).
    //   Entry = the 50% retracement of D-C (= (D+C)/2) - BullishSignal/
    //   BearishSignal fires the bar price wicks back to touch it.
    //   Invalidated at any point (after A) if price closes back through A.
    //
    // A fresh trigger for a direction always restarts that direction's
    // sequence from the new A, abandoning any stale in-progress attempt.
    //
    // OUTPUTS (the Robot's entire interface into this indicator):
    //   - BullishSignal / BearishSignal: 1 on the bar the D-C 50% entry is
    //     touched (and, if RequireTrendFilter is on, the HTF SMA trend
    //     regime agrees), 0 otherwise.
    //   - StopAnchor: on a signal bar, C (StopLossMode.Conservative) or A
    //     (StopLossMode.Normal). NaN otherwise.
    //   - TargetLevel: on a signal bar, the 261.8% extension of the C->D
    //     leg: C + (D - C) x 2.618. NaN otherwise. This single formula
    //     already produces the correct (mirrored) result for a bearish
    //     setup without needing direction-specific logic.
    // =========================================================================

    public enum StopLossMode
    {
        Conservative,
        Normal
    }


    [Indicator(IsOverlay = true, AccessRights = AccessRights.None)]
    public class SwingBreakoutSFPSignal : Indicator
    {
        // === Shared config - order matches SwingBreakoutTrader.cs's ==========
        // === GetIndicator<SwingBreakoutSFPSignal>(...) call exactly.  ========
        [Parameter("Swing Lookback - Left Bars", DefaultValue = SharedSignalDefaults.SwingLeftBars, MinValue = 1, MaxValue = 50, Group = "Signal")]
        public int SwingLeftBars { get; set; }

        [Parameter("Swing Lookback - Right Bars", DefaultValue = SharedSignalDefaults.SwingRightBars, MinValue = 1, MaxValue = 50, Group = "Signal")]
        public int SwingRightBars { get; set; }

        [Parameter("Track Prior Swing High/Low", DefaultValue = SharedSignalDefaults.TrackPriorSwing, Group = "Levels")]
        public bool TrackPriorSwing { get; set; }

        [Parameter("Track Prior Day High/Low/Close", DefaultValue = SharedSignalDefaults.TrackPDHPDL, Group = "Levels")]
        public bool TrackPDHPDL { get; set; }

        [Parameter("Track Month-to-Date Close High/Low (HCOM/LCOM)", DefaultValue = SharedSignalDefaults.TrackHcomLcom, Group = "Levels")]
        public bool TrackHcomLcom { get; set; }

        // Only used by the SMA trend filter below (TrendFilterOK) - no swing
        // high/low tracking happens on this timeframe.
        [Parameter("Higher Timeframe (for SMA trend filter)", DefaultValue = SharedSignalDefaults.TrendTimeFrame, Group = "Levels")]
        public TimeFrame TrendTimeFrame { get; set; }

        // DIRECTIONAL GATE: this is a REGIME check (which side of the
        // 20/200 SMA pair the market is on), not a requirement that price
        // itself has already cleared both lines on the entry bar.
        [Parameter("Require SMA trend filter (direction + separation)", DefaultValue = SharedSignalDefaults.RequireTrendFilter, Group = "Confluence")]
        public bool RequireTrendFilter { get; set; }

        [Parameter("ATR Period", DefaultValue = SharedSignalDefaults.AtrPeriod, MinValue = 1, Group = "Signal")]
        public int AtrPeriod { get; set; }

        // The wick (SFP) or close (B&R break) must travel at least this far
        // past a level (in units of chart-timeframe ATR) to count - a bare
        // tick through the level is not a genuine reaction.
        [Parameter("Min. Sweep/Break Depth beyond level (x ATR)", DefaultValue = SharedSignalDefaults.MinSweepDepthATRmult, MinValue = 0.0, Step = 0.05, Group = "Signal")]
        public double MinSweepDepthATRmult { get; set; }

        // Conservative -> SL at C (tighter, inside the D-C retracement).
        // Normal -> SL at A (wider, the original swing extreme). See
        // STRATEGY.md.
        [Parameter("Stop-Loss Mode", DefaultValue = SharedSignalDefaults.StopMode, Group = "Signal")]
        public StopLossMode StopMode { get; set; }
        // === End of shared config ==============================================

        // === Confidence Dots - NOT part of shared config/GetIndicator<T>(). ===
        // The robot can consume the green/red dot outputs when its own
        // Enable Confidence Mode setting is on. The display setting below
        // affects chart icons only, never the output values.
        // Buyer/seller control, classified from lower-timeframe intrabar
        // candle direction (same technique as the standalone
        // BuyerSellerControlDots indicator), plotted as a dot at confirmed
        // swing pivots only - Major (bigger picture, its own lookback) and
        // Minor (reuses SwingLeftBars/SwingRightBars above, so minor dots
        // land on the same pivots the A-B-C-D sequence itself uses).
        //
        // Only the knobs that change what the dots actually DO are exposed
        // here - everything else (neutral-zone width, lower-timeframe
        // override, rolling-window smoothing, debounce, amber/change/fade
        // filters, dot offset) is a fixed internal constant below; edit
        // those directly in code if you want a different value.
        [Parameter("Show Confidence Dots", DefaultValue = true, Group = "Confidence Dots")]
        public bool ShowConfidenceDots { get; set; }

        [Parameter("Major Swing Lookback - Left Bars", DefaultValue = 15, MinValue = 1, MaxValue = 100, Group = "Confidence Dots")]
        public int MajorSwingLeftBars { get; set; }

        [Parameter("Major Swing Lookback - Right Bars", DefaultValue = 15, MinValue = 1, MaxValue = 100, Group = "Confidence Dots")]
        public int MajorSwingRightBars { get; set; }
        // === End of Confidence Dots =============================================

        // === Consolidation detection is execution-relevant but not shared ===
        // === via GetIndicator<T>(); the Robot reads its output directly.  ===
        // A range is consolidated after ADX stays below its threshold for a
        // minimum duration. Its high/low bounds are tracked for chart review.
        [Parameter("Highlight Consolidation (visual only)", DefaultValue = true, Group = "Consolidation Highlight")]
        public bool HighlightConsolidation { get; set; }

        [Parameter("Consolidation ADX Period", DefaultValue = 14, MinValue = 2, Group = "Consolidation Highlight")]
        public int ConsolidationAdxPeriod { get; set; }

        [Parameter("Consolidation Max ADX", DefaultValue = 17.0, MinValue = 1.0, Step = 1.0, Group = "Consolidation Highlight")]
        public double ConsolidationMaxAdx { get; set; }

        [Parameter("Consolidation Min. Bars", DefaultValue = 15, MinValue = 2, Group = "Consolidation Highlight")]
        public int ConsolidationMinBars { get; set; }
        // === End of Consolidation Highlight ======================================

        // Internal-only tuning, not shared with the bot - edit directly for
        // a different value. Used by the trend filter, against the
        // HIGHER-timeframe ATR (see ComputeAtr/_htfSma20/_htfSma200) - not
        // the chart's own _atr, since the SMAs it measures the separation
        // of live on that longer horizon too.
        private const double TrendSeparationATRmult = 0.5;

        // 261.8% extension ratio used for TargetLevel - see STRATEGY.md.
        private const double TargetExtensionRatio = 2.618;

        // Confidence Dots internal tuning - see that parameter group's
        // comment for why these aren't [Parameter]s. Edit directly for a
        // different value.
        //
        // A green/red dot requires buy volume above 70% or below 30%. Amber
        // marks the neutral band. Opacity rises from 20% at neutral to 100%
        // at full directional conviction.
        private const double ConfidenceNeutralBandPct = 40.0;
        private const bool ConfidenceShowAmberDots = true;
        private const bool ConfidenceOnlyOnChange = true;
        private const bool ConfidenceFadeByConviction = true;
        private const double ConfidenceDotOffsetATRmult = 0.5;

        private static readonly Color BullColor = Color.FromArgb(255, 0, 200, 120);
        private static readonly Color BearColor = Color.FromArgb(255, 220, 60, 60);
        private static readonly Color BreakoutColor = Color.FromArgb(255, 255, 165, 0);
        private static readonly Color SwingLowLineColor = Color.FromArgb(160, 0, 200, 120);
        private static readonly Color SwingHighLineColor = Color.FromArgb(160, 220, 60, 60);
        private static readonly Color ConsolidationColor = Color.FromArgb(45, 255, 193, 7);

        [Output("Bullish Signal", LineColor = "Transparent")]
        public IndicatorDataSeries BullishSignal { get; set; }

        [Output("Bearish Signal", LineColor = "Transparent")]
        public IndicatorDataSeries BearishSignal { get; set; }

        [Output("Stop Anchor", LineColor = "Transparent")]
        public IndicatorDataSeries StopAnchor { get; set; }

        [Output("Target Level", LineColor = "Transparent")]
        public IndicatorDataSeries TargetLevel { get; set; }

        [Output("Confidence Buy", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceBuySignal { get; set; }

        [Output("Confidence Sell", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceSellSignal { get; set; }

        [Output("Confidence Stop", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceStopAnchor { get; set; }

        [Output("Confidence Target", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceTargetLevel { get; set; }

        [Output("Consolidation Active", LineColor = "Transparent")]
        public IndicatorDataSeries ConsolidationActive { get; set; }

        private AverageTrueRange _atr;
        private DirectionalMovementSystem _consolidationDms;
        // Trend filter SMAs - deliberately computed on the HIGHER
        // timeframe's own bars (_htfBars), not the chart's, so "trend" is
        // measured on the same horizon as the structure being swept.
        private SimpleMovingAverage _htfSma20;
        private SimpleMovingAverage _htfSma200;
        private Bars _dailyBars;
        private Bars _htfBars;

        // Repaint-safe persisted state - full IndicatorDataSeries, recomputed
        // fresh from index-1 on every Calculate(index) call rather than
        // mutated in place, so re-calculation (live bar repainting, a full
        // chart reload) always lands on the same result for a closed bar.
        private IndicatorDataSeries _lastSwingHighSeries;
        private IndicatorDataSeries _lastSwingLowSeries;
        private IndicatorDataSeries _lastSwingHighBarSeries;
        private IndicatorDataSeries _lastSwingLowBarSeries;
        private IndicatorDataSeries _swingHighBrokenSeries;
        private IndicatorDataSeries _swingLowBrokenSeries;

        // Consolidation Highlight state - see that parameter group's
        // comment and UpdateConsolidationHighlight below. _consolRunHigh/Low
        // carry the running high/low of the CURRENT active run so extending
        // it stays O(1) per bar instead of rescanning the whole run.
        private IndicatorDataSeries _consolActive;
        private IndicatorDataSeries _consolCandidate;
        private IndicatorDataSeries _consolRunStart;
        private IndicatorDataSeries _consolRunHigh;
        private IndicatorDataSeries _consolRunLow;

        // B&R pending-break tracking, per direction (index 0 = bullish,
        // index 1 = bearish). A single tracker per direction, not per level
        // type - the first enabled level whose break condition fires arms
        // it (mirrors how SFP already ORs across enabled levels). See
        // STRATEGY.md's B&R section.
        private IndicatorDataSeries[] _bnrPending = new IndicatorDataSeries[2];
        private IndicatorDataSeries[] _bnrLevel = new IndicatorDataSeries[2];

        // A-B-C-D sequence state, per direction (index 0 = bullish,
        // index 1 = bearish). State values: 0 Idle, 1 SeekingB, 2 SeekingC,
        // 3 SeekingExtensionBreak, 4 SeekingD, 5 SeekingEntry.
        private IndicatorDataSeries[] _seqState = new IndicatorDataSeries[2];
        private IndicatorDataSeries[] _seqPhaseStartBar = new IndicatorDataSeries[2];
        private IndicatorDataSeries[] _seqA = new IndicatorDataSeries[2];
        private IndicatorDataSeries[] _seqB = new IndicatorDataSeries[2];
        private IndicatorDataSeries[] _seqC = new IndicatorDataSeries[2];
        private IndicatorDataSeries[] _seqD = new IndicatorDataSeries[2];

        private const double StIdle = 0;
        private const double StSeekingB = 1;
        private const double StSeekingC = 2;
        private const double StSeekingExt = 3;
        private const double StSeekingD = 4;
        private const double StSeekingEntry = 5;

        // Confidence Dots state - see the "Confidence Dots" parameter group
        // and DrawSwingDot/UpdateConfidenceDots below. Ported from the
        // standalone BuyerSellerControlDots indicator's volume-delta
        // classification, then anchored to swing pivots instead of every bar.
        private Bars _confLowerBars; // null when no lower timeframe is in play
        private IndicatorDataSeries _lastMajorDotCode, _lastMinorDotCode;

        // ChartIcon has no native hover tooltip in cTrader's desktop client
        // (ChartObject.Comment is metadata only, not rendered), so confidence
        // percentage on hover is implemented by hand: each drawn dot's
        // position/value is recorded here, and Chart.MouseMove hit-tests the
        // cursor against them to show/hide one reused ChartText label.
        private readonly List<(int barIndex, DateTime time, double y, double pct, double radius)> _confidenceDotHits =
            new List<(int, DateTime, double, double, double)>();
        private ChartText _confidenceHoverLabel;

        protected override void Initialize()
        {
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.WilderSmoothing);
            _consolidationDms = Indicators.DirectionalMovementSystem(ConsolidationAdxPeriod);
            _dailyBars = MarketData.GetBars(TimeFrame.Daily);
            _htfBars = MarketData.GetBars(TrendTimeFrame);
            // Must come after _htfBars is assigned above - these read its ClosePrices.
            _htfSma20 = Indicators.SimpleMovingAverage(_htfBars.ClosePrices, 20);
            _htfSma200 = Indicators.SimpleMovingAverage(_htfBars.ClosePrices, 200);

            _lastSwingHighSeries = CreateDataSeries();
            _lastSwingLowSeries = CreateDataSeries();
            _lastSwingHighBarSeries = CreateDataSeries();
            _lastSwingLowBarSeries = CreateDataSeries();
            _swingHighBrokenSeries = CreateDataSeries();
            _swingLowBrokenSeries = CreateDataSeries();

            _consolActive = CreateDataSeries();
            _consolCandidate = CreateDataSeries();
            _consolRunStart = CreateDataSeries();
            _consolRunHigh = CreateDataSeries();
            _consolRunLow = CreateDataSeries();

            for (int i = 0; i < 2; i++)
            {
                _bnrPending[i] = CreateDataSeries();
                _bnrLevel[i] = CreateDataSeries();
                _seqState[i] = CreateDataSeries();
                _seqPhaseStartBar[i] = CreateDataSeries();
                _seqA[i] = CreateDataSeries();
                _seqB[i] = CreateDataSeries();
                _seqC[i] = CreateDataSeries();
                _seqD[i] = CreateDataSeries();
            }

            var confLowerTf = GetAutoConfidenceLowerTimeframe();
            _confLowerBars = confLowerTf != null ? MarketData.GetBars(confLowerTf) : null;

            _lastMajorDotCode = CreateDataSeries();
            _lastMinorDotCode = CreateDataSeries();

            Chart.MouseMove += OnChartMouseMove;
        }

        public override void Calculate(int index)
        {
            BullishSignal[index] = 0;
            BearishSignal[index] = 0;
            StopAnchor[index] = double.NaN;
            TargetLevel[index] = double.NaN;
            ConfidenceBuySignal[index] = 0;
            ConfidenceSellSignal[index] = 0;
            ConfidenceStopAnchor[index] = double.NaN;
            ConfidenceTargetLevel[index] = double.NaN;
            ConsolidationActive[index] = 0;

            double lastSwingHigh = index > 0 ? _lastSwingHighSeries[index - 1] : double.NaN;
            double lastSwingLow = index > 0 ? _lastSwingLowSeries[index - 1] : double.NaN;
            double lastSwingHighBar = index > 0 ? _lastSwingHighBarSeries[index - 1] : double.NaN;
            double lastSwingLowBar = index > 0 ? _lastSwingLowBarSeries[index - 1] : double.NaN;
            bool swingHighBroken = index > 0 && _swingHighBrokenSeries[index - 1] > 0.5;
            bool swingLowBroken = index > 0 && _swingLowBrokenSeries[index - 1] > 0.5;

            double barHigh = Bars.HighPrices[index];
            double barLow = Bars.LowPrices[index];
            double barClose = Bars.ClosePrices[index];
            double atrVal = _atr.Result[index];
            var levels = GetKeyLevels(index);

            // Breakout check against whichever swing level was ACTIVE coming
            // into this bar (i.e. before this bar's own candidate pivot, a
            // few lines down, is allowed to replace it): a close beyond it
            // means structure gave way. Runs UNCONDITIONALLY (not gated by
            // TrackPriorSwing) because swingHighBroken/swingLowBroken drive
            // which pivot is allowed to replace lastSwingHigh/lastSwingLow
            // below. Only the icon drawing stays behind the toggle.
            if (!double.IsNaN(atrVal) && atrVal > 0)
            {
                if (!double.IsNaN(lastSwingLow) && !swingLowBroken && barClose < lastSwingLow)
                {
                    swingLowBroken = true;
                    if (TrackPriorSwing)
                        Chart.DrawIcon("BreakLow" + index, ChartIconType.Circle, index, barLow - atrVal * 0.5, BreakoutColor);
                }
                if (!double.IsNaN(lastSwingHigh) && !swingHighBroken && barClose > lastSwingHigh)
                {
                    swingHighBroken = true;
                    if (TrackPriorSwing)
                        Chart.DrawIcon("BreakHigh" + index, ChartIconType.Circle, index, barHigh + atrVal * 0.5, BreakoutColor);
                }
            }

            // Keep the MORE SIGNIFICANT (higher high / lower low) of the two
            // swings until it's actually broken, rather than jumping to
            // every freshly confirmed pivot regardless of size.
            int candidate = index - SwingRightBars;
            if (candidate >= 0)
            {
                if (IsPivotHigh(candidate, SwingLeftBars, SwingRightBars))
                {
                    double candidateHigh = Bars.HighPrices[candidate];
                    if (swingHighBroken || double.IsNaN(lastSwingHigh) || candidateHigh > lastSwingHigh)
                    {
                        lastSwingHigh = candidateHigh;
                        lastSwingHighBar = candidate;
                        swingHighBroken = false;
                    }
                }
                if (IsPivotLow(candidate, SwingLeftBars, SwingRightBars))
                {
                    double candidateLow = Bars.LowPrices[candidate];
                    if (swingLowBroken || double.IsNaN(lastSwingLow) || candidateLow < lastSwingLow)
                    {
                        lastSwingLow = candidateLow;
                        lastSwingLowBar = candidate;
                        swingLowBroken = false;
                    }
                }
            }
            _lastSwingHighSeries[index] = lastSwingHigh;
            _lastSwingLowSeries[index] = lastSwingLow;
            _lastSwingHighBarSeries[index] = lastSwingHighBar;
            _lastSwingLowBarSeries[index] = lastSwingLowBar;
            _swingHighBrokenSeries[index] = swingHighBroken ? 1 : 0;
            _swingLowBrokenSeries[index] = swingLowBroken ? 1 : 0;

            // Draw/extend the active (unbroken) swing-low/high level line.
            if (TrackPriorSwing)
            {
                if (!double.IsNaN(lastSwingLow) && !double.IsNaN(lastSwingLowBar) && !swingLowBroken)
                    Chart.DrawTrendLine("SwingLowLine", (int)lastSwingLowBar, lastSwingLow, index, lastSwingLow, SwingLowLineColor);
                if (!double.IsNaN(lastSwingHigh) && !double.IsNaN(lastSwingHighBar) && !swingHighBroken)
                    Chart.DrawTrendLine("SwingHighLine", (int)lastSwingHighBar, lastSwingHigh, index, lastSwingHigh, SwingHighLineColor);
            }

            // === STEP 1: level reaction - SFP (single-bar) and B&R =========
            // === (break now, retest whenever it happens). ==================
            bool bearishSFP = false;
            bool bullishSFP = false;

            if (TrackPriorSwing && BearishSweepOK(barHigh, barClose, lastSwingHigh, atrVal))
                bearishSFP = true;
            if (TrackPDHPDL && BearishSweepOK(barHigh, barClose, levels.Pdh, atrVal))
                bearishSFP = true;
            if (TrackPDHPDL && BearishSweepOK(barHigh, barClose, levels.Pdc, atrVal))
                bearishSFP = true;
            if (TrackHcomLcom && BearishSweepOK(barHigh, barClose, levels.Hcom, atrVal))
                bearishSFP = true;

            if (TrackPriorSwing && BullishSweepOK(barLow, barClose, lastSwingLow, atrVal))
                bullishSFP = true;
            if (TrackPDHPDL && BullishSweepOK(barLow, barClose, levels.Pdl, atrVal))
                bullishSFP = true;
            if (TrackPDHPDL && BullishSweepOK(barLow, barClose, levels.Pdc, atrVal))
                bullishSFP = true;
            if (TrackHcomLcom && BullishSweepOK(barLow, barClose, levels.Lcom, atrVal))
                bullishSFP = true;

            bool bullishBnR = ProcessBnR(0, 1, index, barLow, barClose, lastSwingHigh, lastSwingLow, levels, atrVal);
            bool bearishBnR = ProcessBnR(1, -1, index, barHigh, barClose, lastSwingHigh, lastSwingLow, levels, atrVal);

            bool bullishTriggered = bullishSFP || bullishBnR;
            bool bearishTriggered = bearishSFP || bearishBnR;

            // === STEP 2: A-B-C-D sequence per direction, independently. ===
            ProcessSequence(0, 1, index, bullishTriggered, barHigh, barLow, barClose, BullishSignal);
            ProcessSequence(1, -1, index, bearishTriggered, barHigh, barLow, barClose, BearishSignal);

            if (BearishSignal[index] > 0.5)
                Chart.DrawIcon("SFPBear" + index, ChartIconType.Circle, index, barHigh + atrVal * 0.3, BearColor);
            if (BullishSignal[index] > 0.5)
                Chart.DrawIcon("SFPBull" + index, ChartIconType.Circle, index, barLow - atrVal * 0.3, BullColor);

            UpdateConfidenceDots(index);

            UpdateConsolidationState(index);
        }

        // Single pending-break tracker for `dir` (bullish=1, bearish=-1).
        // While not pending, looks for a fresh close-beyond-level break on
        // any enabled level (FindBrokenLevel). Once pending, waits
        // (unbounded) for either a retest (wick back to the broken level,
        // close still holding beyond it - B&R CONFIRMED, returns true) or
        // an invalidation (close back through the broken level - pending
        // cleared, no B&R). See STRATEGY.md.
        private bool ProcessBnR(int dirIdx, int dir, int index, double barExtreme, double barClose,
            double lastSwingHigh, double lastSwingLow, KeyLevels levels, double atrVal)
        {
            bool pending = index > 0 && _bnrPending[dirIdx][index - 1] > 0.5;
            double brokenLevel = index > 0 ? _bnrLevel[dirIdx][index - 1] : double.NaN;
            bool confirmed = false;

            if (!double.IsNaN(atrVal) && atrVal > 0)
            {
                if (pending)
                {
                    bool retested = dir == 1
                        ? (barExtreme <= brokenLevel && barClose > brokenLevel)
                        : (barExtreme >= brokenLevel && barClose < brokenLevel);
                    bool failed = dir == 1 ? barClose < brokenLevel : barClose > brokenLevel;

                    if (retested)
                    {
                        confirmed = true;
                        pending = false;
                        brokenLevel = double.NaN;
                    }
                    else if (failed)
                    {
                        pending = false;
                        brokenLevel = double.NaN;
                    }
                }
                else
                {
                    double? broken = FindBrokenLevel(dir, lastSwingHigh, lastSwingLow, levels, barClose, atrVal);
                    if (broken.HasValue)
                    {
                        pending = true;
                        brokenLevel = broken.Value;
                    }
                }
            }

            _bnrPending[dirIdx][index] = pending ? 1 : 0;
            _bnrLevel[dirIdx][index] = brokenLevel;
            return confirmed;
        }

        // First enabled level (in this fixed priority order: swing, PDH/PDL,
        // PDC, HCOM/LCOM) whose close-beyond-level break condition fires
        // this bar, or null. Mirrors how SFP already ORs across enabled
        // levels rather than tracking each independently.
        private double? FindBrokenLevel(int dir, double lastSwingHigh, double lastSwingLow, KeyLevels levels, double barClose, double atrVal)
        {
            double threshold = MinSweepDepthATRmult * atrVal;
            if (dir == 1)
            {
                if (TrackPriorSwing && !double.IsNaN(lastSwingHigh) && barClose > lastSwingHigh + threshold) return lastSwingHigh;
                if (TrackPDHPDL && !double.IsNaN(levels.Pdh) && barClose > levels.Pdh + threshold) return levels.Pdh;
                if (TrackPDHPDL && !double.IsNaN(levels.Pdc) && barClose > levels.Pdc + threshold) return levels.Pdc;
                if (TrackHcomLcom && !double.IsNaN(levels.Hcom) && barClose > levels.Hcom + threshold) return levels.Hcom;
            }
            else
            {
                if (TrackPriorSwing && !double.IsNaN(lastSwingLow) && barClose < lastSwingLow - threshold) return lastSwingLow;
                if (TrackPDHPDL && !double.IsNaN(levels.Pdl) && barClose < levels.Pdl - threshold) return levels.Pdl;
                if (TrackPDHPDL && !double.IsNaN(levels.Pdc) && barClose < levels.Pdc - threshold) return levels.Pdc;
                if (TrackHcomLcom && !double.IsNaN(levels.Lcom) && barClose < levels.Lcom - threshold) return levels.Lcom;
            }
            return null;
        }

        // The A-B-C-D-entry state machine for `dir` (bullish=1, bearish=-1).
        // A fresh trigger always (re)starts the sequence from the new A,
        // abandoning any stale in-progress attempt. See STRATEGY.md for the
        // full sequence and the diagram of every step.
        private void ProcessSequence(int dirIdx, int dir, int index, bool triggered,
            double barHigh, double barLow, double barClose, IndicatorDataSeries signalSeries)
        {
            double state = index > 0 ? _seqState[dirIdx][index - 1] : StIdle;
            double phaseStartBar = index > 0 ? _seqPhaseStartBar[dirIdx][index - 1] : double.NaN;
            double a = index > 0 ? _seqA[dirIdx][index - 1] : double.NaN;
            double b = index > 0 ? _seqB[dirIdx][index - 1] : double.NaN;
            double c = index > 0 ? _seqC[dirIdx][index - 1] : double.NaN;
            double d = index > 0 ? _seqD[dirIdx][index - 1] : double.NaN;

            if (triggered)
            {
                state = StSeekingB;
                phaseStartBar = index;
                a = dir == 1 ? barLow : barHigh;
                b = double.NaN;
                c = double.NaN;
                d = double.NaN;
            }
            else if (state != StIdle)
            {
                bool brokeA = dir == 1 ? barClose < a : barClose > a;

                if (state != StSeekingB && brokeA)
                {
                    // Structure invalidated - price closed back through the
                    // original A before an entry ever fired. Abandon.
                    state = StIdle;
                    phaseStartBar = double.NaN;
                    a = double.NaN; b = double.NaN; c = double.NaN; d = double.NaN;
                }
                else if (state == StSeekingB)
                {
                    // Still forming the first leg - track the most extreme
                    // point as the true anchor instead of getting stuck on a
                    // stale one.
                    if (dir == 1 ? barLow < a : barHigh > a)
                        a = dir == 1 ? barLow : barHigh;

                    int candidate = index - SwingRightBars;
                    if (candidate > phaseStartBar)
                    {
                        bool isPivot = dir == 1 ? IsPivotHigh(candidate, SwingLeftBars, SwingRightBars) : IsPivotLow(candidate, SwingLeftBars, SwingRightBars);
                        if (isPivot)
                        {
                            double candidatePrice = dir == 1 ? Bars.HighPrices[candidate] : Bars.LowPrices[candidate];
                            bool beyondA = dir == 1 ? candidatePrice > a : candidatePrice < a;
                            if (beyondA)
                            {
                                b = candidatePrice;
                                state = StSeekingC;
                                phaseStartBar = candidate;
                            }
                        }
                    }
                }
                else if (state == StSeekingC)
                {
                    int candidate = index - SwingRightBars;
                    if (candidate > phaseStartBar)
                    {
                        // A fresh, more extreme B extends the first leg
                        // rather than being treated as a retracement.
                        bool isPivotB = dir == 1 ? IsPivotHigh(candidate, SwingLeftBars, SwingRightBars) : IsPivotLow(candidate, SwingLeftBars, SwingRightBars);
                        if (isPivotB)
                        {
                            double candidateExtreme = dir == 1 ? Bars.HighPrices[candidate] : Bars.LowPrices[candidate];
                            bool extendsB = dir == 1 ? candidateExtreme > b : candidateExtreme < b;
                            if (extendsB)
                                b = candidateExtreme;
                        }

                        bool isPivotC = dir == 1 ? IsPivotLow(candidate, SwingLeftBars, SwingRightBars) : IsPivotHigh(candidate, SwingLeftBars, SwingRightBars);
                        if (isPivotC)
                        {
                            double candidatePrice = dir == 1 ? Bars.LowPrices[candidate] : Bars.HighPrices[candidate];
                            bool isValidRetracement = dir == 1 ? (candidatePrice < b && candidatePrice > a) : (candidatePrice > b && candidatePrice < a);
                            if (isValidRetracement)
                            {
                                c = candidatePrice;
                                state = StSeekingExt;
                                phaseStartBar = candidate;
                            }
                        }
                    }
                }
                else if (state == StSeekingExt)
                {
                    double ext100 = c + (b - a);
                    bool broke = dir == 1 ? barClose > ext100 : barClose < ext100;
                    if (broke)
                    {
                        state = StSeekingD;
                        phaseStartBar = index;
                    }
                }
                else if (state == StSeekingD)
                {
                    int candidate = index - SwingRightBars;
                    if (candidate > phaseStartBar)
                    {
                        bool isPivot = dir == 1 ? IsPivotHigh(candidate, SwingLeftBars, SwingRightBars) : IsPivotLow(candidate, SwingLeftBars, SwingRightBars);
                        if (isPivot)
                        {
                            d = dir == 1 ? Bars.HighPrices[candidate] : Bars.LowPrices[candidate];
                            state = StSeekingEntry;
                            phaseStartBar = candidate;
                        }
                    }
                }
                else if (state == StSeekingEntry)
                {
                    double entryPrice = (d + c) / 2.0;
                    bool touched = dir == 1 ? barLow <= entryPrice : barHigh >= entryPrice;
                    if (touched && TrendFilterOK(dir, index))
                    {
                        signalSeries[index] = 1;
                        StopAnchor[index] = StopMode == StopLossMode.Conservative ? c : a;
                        TargetLevel[index] = c + (d - c) * TargetExtensionRatio;

                        state = StIdle;
                        phaseStartBar = double.NaN;
                        a = double.NaN; b = double.NaN; c = double.NaN; d = double.NaN;
                    }
                }
            }

            _seqState[dirIdx][index] = state;
            _seqPhaseStartBar[dirIdx][index] = phaseStartBar;
            _seqA[dirIdx][index] = a;
            _seqB[dirIdx][index] = b;
            _seqC[dirIdx][index] = c;
            _seqD[dirIdx][index] = d;
        }

        // SMA trend + separation (on the higher timeframe - see
        // _htfSma20/_htfSma200), evaluated at same-bar entry time.
        private bool TrendFilterOK(int dir, int index)
        {
            if (!RequireTrendFilter)
                return true;

            int htfIdx = FindBarIndexForTime(_htfBars, Bars.OpenTimes[index]);
            if (htfIdx < 0)
                return false;

            double htfSma20 = _htfSma20.Result[htfIdx];
            double htfSma200 = _htfSma200.Result[htfIdx];
            if (double.IsNaN(htfSma20) || double.IsNaN(htfSma200))
                return false;

            bool smaTrendOK = dir == 1
                ? Bars.ClosePrices[index] > htfSma200 && htfSma20 > htfSma200
                : Bars.ClosePrices[index] < htfSma200 && htfSma20 < htfSma200;
            if (!smaTrendOK)
                return false;

            double htfAtrVal = ComputeAtr(_htfBars, htfIdx, AtrPeriod);
            if (double.IsNaN(htfAtrVal) || htfAtrVal <= 0)
                return false;

            double smaSeparation = Math.Abs(htfSma20 - htfSma200);
            return smaSeparation >= TrendSeparationATRmult * htfAtrVal;
        }

        // A plain (simple-averaged, not Wilder-smoothed) True Range average
        // over the last `period` bars ending at endIndex, computed fresh
        // each call rather than through a persisted indicator. Used only for
        // the trend-separation check above, which needs an ATR reading on
        // the SAME bars as _htfSma20/_htfSma200 (the higher timeframe).
        private double ComputeAtr(Bars bars, int endIndex, int period)
        {
            if (endIndex - period < 0)
                return double.NaN;

            double sum = 0;
            for (int i = endIndex - period + 1; i <= endIndex; i++)
            {
                double high = bars.HighPrices[i];
                double low = bars.LowPrices[i];
                double prevClose = bars.ClosePrices[i - 1];
                double tr = Math.Max(high - low, Math.Max(Math.Abs(high - prevClose), Math.Abs(low - prevClose)));
                sum += tr;
            }
            return sum / period;
        }

        private bool BearishSweepOK(double barHigh, double barClose, double level, double atrVal)
        {
            if (double.IsNaN(level) || !(barHigh > level && barClose < level))
                return false;

            double depth = barHigh - level;
            return depth >= MinSweepDepthATRmult * atrVal;
        }

        private bool BullishSweepOK(double barLow, double barClose, double level, double atrVal)
        {
            if (double.IsNaN(level) || !(barLow < level && barClose > level))
                return false;

            double depth = level - barLow;
            return depth >= MinSweepDepthATRmult * atrVal;
        }

        private struct KeyLevels
        {
            public double Pdh, Pdl, Pdc;
            public double Hcom, Lcom;
        }

        private KeyLevels GetKeyLevels(int index)
        {
            var result = new KeyLevels
            {
                Pdh = double.NaN,
                Pdl = double.NaN,
                Pdc = double.NaN,
                Hcom = double.NaN,
                Lcom = double.NaN
            };
            if (_dailyBars.Count == 0)
                return result;

            DateTime barTime = Bars.OpenTimes[index];
            int dIdx = FindBarIndexForTime(_dailyBars, barTime);
            if (dIdx < 0)
                return result;

            int prevDayIdx = dIdx - 1;
            if (prevDayIdx >= 0)
            {
                result.Pdh = _dailyBars.HighPrices[prevDayIdx];
                result.Pdl = _dailyBars.LowPrices[prevDayIdx];
                result.Pdc = _dailyBars.ClosePrices[prevDayIdx];
            }

            DateTime refDate = _dailyBars.OpenTimes[dIdx].Date;
            int month = refDate.Month, year = refDate.Year;
            double hi = double.NegativeInfinity, lo = double.PositiveInfinity;
            for (int i = dIdx; i >= 0; i--)
            {
                DateTime t = _dailyBars.OpenTimes[i].Date;
                if (t.Month != month || t.Year != year) break;
                double c = _dailyBars.ClosePrices[i];
                if (c > hi) hi = c;
                if (c < lo) lo = c;
            }
            if (!double.IsInfinity(hi))
            {
                result.Hcom = hi;
                result.Lcom = lo;
            }

            return result;
        }

        // Generic time->index binary search - used for _dailyBars (PDH/PDL/
        // PDC/HCOM/LCOM lookups) and for _htfBars (trend-filter SMA lookups).
        private int FindBarIndexForTime(Bars bars, DateTime time)
        {
            int lo = 0, hi = bars.Count - 1, result = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (bars.OpenTimes[mid] <= time) { result = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return result;
        }

        private bool IsPivotHigh(int centerIndex, int left, int right)
        {
            if (centerIndex - left < 0 || centerIndex + right >= Bars.Count)
                return false;

            double centerHigh = Bars.HighPrices[centerIndex];
            for (int i = centerIndex - left; i <= centerIndex + right; i++)
            {
                if (i == centerIndex) continue;
                if (Bars.HighPrices[i] >= centerHigh) return false;
            }
            return true;
        }

        private bool IsPivotLow(int centerIndex, int left, int right)
        {
            if (centerIndex - left < 0 || centerIndex + right >= Bars.Count)
                return false;

            double centerLow = Bars.LowPrices[centerIndex];
            for (int i = centerIndex - left; i <= centerIndex + right; i++)
            {
                if (i == centerIndex) continue;
                if (Bars.LowPrices[i] <= centerLow) return false;
            }
            return true;
        }

        // =====================================================================
        // ADX identifies the low-trend regime. A candidate becomes active only
        // once it survives ConsolidationMinBars bars, filtering brief dips.
        // =====================================================================

        private void UpdateConsolidationState(int index)
        {
            bool wasCandidate = index > 0 && _consolCandidate[index - 1] > 0.5;
            double runStart = index > 0 ? _consolRunStart[index - 1] : double.NaN;
            double runHigh = index > 0 ? _consolRunHigh[index - 1] : double.NaN;
            double runLow = index > 0 ? _consolRunLow[index - 1] : double.NaN;
            double adx = _consolidationDms.ADX[index];
            bool isCandidate = !double.IsNaN(adx) && adx < ConsolidationMaxAdx;

            if (isCandidate)
            {
                if (!wasCandidate)
                {
                    runStart = index;
                    runHigh = Bars.HighPrices[index];
                    runLow = Bars.LowPrices[index];
                }
                else
                {
                    runHigh = Math.Max(runHigh, Bars.HighPrices[index]);
                    runLow = Math.Min(runLow, Bars.LowPrices[index]);
                }
            }

            bool isConsolidating = isCandidate && index - (int)runStart + 1 >= ConsolidationMinBars;
            if (isConsolidating && HighlightConsolidation)
            {
                var rect = Chart.DrawRectangle("Consolidation_" + (int)runStart, (int)runStart, runHigh, index, runLow, ConsolidationColor);
                rect.IsFilled = true;
            }

            ConsolidationActive[index] = isConsolidating ? 1 : 0;
            _consolActive[index] = isConsolidating ? 1 : 0;
            _consolCandidate[index] = isCandidate ? 1 : 0;
            _consolRunStart[index] = isCandidate ? runStart : double.NaN;
            _consolRunHigh[index] = isCandidate ? runHigh : double.NaN;
            _consolRunLow[index] = isCandidate ? runLow : double.NaN;
        }

        // =====================================================================
        // Confidence Dots - buyer/seller control classification, ported from
        // the standalone BuyerSellerControlDots indicator, then anchored to
        // swing pivots (Major/Minor) instead of every bar. See the
        // "Confidence Dots" parameter group and the class header.
        // =====================================================================

        private void UpdateConfidenceDots(int index)
        {
            // Major uses its own lookback; Minor reuses SwingLeftBars/
            // SwingRightBars so its dots land on the same pivots the A-B-C-D
            // sequence itself uses.
            DrawConfidenceSwingDot(index, MajorSwingLeftBars, MajorSwingRightBars, ChartIconType.Star, _lastMajorDotCode);
            DrawConfidenceSwingDot(index, SwingLeftBars, SwingRightBars, ChartIconType.Circle, _lastMinorDotCode);
        }

        // Checks for a confirmed swing pivot (high or low) of the given
        // left/right lookback at `index`, and if found, draws a confidence dot
        // at that pivot's own bar/price - offset above a pivot high, below a
        // pivot low. lastDotCodeSeries carries forward the code of the last
        // dot actually drawn on this tier, so the "only on change" rule
        // compares against it independently of the other tier.
        //
        // BUG FIX: this used to color the dot from the volume delta of the
        // pivot bar ALONE. That's systematically wrong - a swing low's own
        // candle is, almost by definition, still a down/sell-dominated
        // candle (that's what pushed price to a new low); the actual buying
        // reaction only shows up on the bars AFTER it, which is exactly the
        // confirmation window (left..right) this pivot test already waits
        // for. Reading only the pivot bar meant lows were reliably
        // classified red and highs reliably green - the opposite of useful
        // "did the reversal have real conviction" information. Now the
        // color comes from the AGGREGATE buy/sell volume across the whole
        // [pivot bar, confirmation bar] window instead.
        private void DrawConfidenceSwingDot(int index, int left, int right, ChartIconType iconType, IndicatorDataSeries lastDotCodeSeries)
        {
            double lastDotCode = index > 0 ? lastDotCodeSeries[index - 1] : double.NaN;

            int candidate = index - right;
            bool isPivotHigh = candidate >= 0 && IsPivotHigh(candidate, left, right);
            bool isPivotLow = candidate >= 0 && IsPivotLow(candidate, left, right);

            if (isPivotHigh || isPivotLow)
            {
                double buyPctWindow = GetWindowBuyPct(candidate, index);
                int code = ClassifyConfidenceCode(buyPctWindow);

                bool amberFilterPass = ConfidenceShowAmberDots || code != 0;
                bool changed = double.IsNaN(lastDotCode) || (int)lastDotCode != code;

                if (amberFilterPass && (!ConfidenceOnlyOnChange || changed))
                {
                    double conviction = Math.Min(Math.Abs(buyPctWindow - 50.0) / 50.0, 1.0);
                    double opacityPct = ConfidenceFadeByConviction
                        ? 20.0 + conviction * 80.0
                        : 100.0;
                    int alpha = ConfidenceFadeByConviction
                        ? (int)Math.Round(255.0 * opacityPct / 100.0)
                        : 255;
                    alpha = Math.Max(0, Math.Min(255, alpha));

                    Color dotColor;
                    if (code == -1)
                        dotColor = Color.FromArgb(alpha, 255, 0, 0);      // red
                    else if (code == 1)
                        dotColor = Color.FromArgb(alpha, 0, 255, 0);      // lime
                    else
                        dotColor = Color.FromArgb(alpha, 255, 179, 0);    // #FFB300 amber

                    SetConfidenceTradeSignal(index, candidate, code);

                    if (ShowConfidenceDots)
                    {
                        double atrAtPivot = _atr.Result[candidate];
                        double offset = atrAtPivot * ConfidenceDotOffsetATRmult;
                        double dotY = isPivotHigh ? Bars.HighPrices[candidate] + offset : Bars.LowPrices[candidate] - offset;

                        string dotName = "conf" + iconType + "Dot_" + candidate;
                        Chart.DrawIcon(dotName, iconType, Bars.OpenTimes[candidate], dotY, dotColor);

                        _confidenceDotHits.Add((candidate, Bars.OpenTimes[candidate], dotY, buyPctWindow, offset * 1.5));
                        if (_confidenceDotHits.Count > 500)
                            _confidenceDotHits.RemoveAt(0);
                    }

                    lastDotCode = code;
                }
            }

            lastDotCodeSeries[index] = lastDotCode;
        }

        // Shows/hides the one reused hover label by hit-testing the cursor's
        // bar/price against every recorded confidence dot. Nearest-bar match
        // within the dot's own draw offset counts as a hit.
        private void OnChartMouseMove(ChartMouseEventArgs args)
        {
            int hoverBar = (int)Math.Round(args.BarIndex);
            double hoverY = args.YValue;

            for (int i = _confidenceDotHits.Count - 1; i >= 0; i--)
            {
                var hit = _confidenceDotHits[i];
                if (Math.Abs(hit.barIndex - hoverBar) <= 1 && Math.Abs(hit.y - hoverY) <= hit.radius)
                {
                    if (_confidenceHoverLabel == null)
                        _confidenceHoverLabel = Chart.DrawText("ConfidenceHoverTip", "", hit.time, hit.y, Color.White);

                    _confidenceHoverLabel.Time = hit.time;
                    _confidenceHoverLabel.Y = hit.y;
                    _confidenceHoverLabel.Text = $"Confidence: {hit.pct:F0}%";
                    _confidenceHoverLabel.IsHidden = false;
                    return;
                }
            }

            if (_confidenceHoverLabel != null)
                _confidenceHoverLabel.IsHidden = true;
        }

        // A dot is only actionable once its pivot has been confirmed. Its
        // stop spans the entire pivot-to-confirmation window; target is 2R.
        private void SetConfidenceTradeSignal(int index, int pivotIndex, int code)
        {
            double entry = Bars.ClosePrices[index];
            double low = double.PositiveInfinity;
            double high = double.NegativeInfinity;

            for (int i = pivotIndex; i <= index; i++)
            {
                low = Math.Min(low, Bars.LowPrices[i]);
                high = Math.Max(high, Bars.HighPrices[i]);
            }

            if (code == 1 && low < entry)
            {
                ConfidenceBuySignal[index] = 1;
                ConfidenceStopAnchor[index] = low;
                ConfidenceTargetLevel[index] = entry + (entry - low) * 2.0;
            }
            else if (code == -1 && high > entry)
            {
                ConfidenceSellSignal[index] = 1;
                ConfidenceStopAnchor[index] = high;
                ConfidenceTargetLevel[index] = entry - (high - entry) * 2.0;
            }
        }

        // Aggregate buy% across bars fromIndex..toIndex inclusive (summing
        // raw buy/sell volume first, not averaging per-bar percentages, so a
        // high-volume bar in the window correctly outweighs a quiet one).
        private double GetWindowBuyPct(int fromIndex, int toIndex)
        {
            double buyVol = 0.0, sellVol = 0.0;
            for (int i = fromIndex; i <= toIndex; i++)
            {
                var split = GetConfLowerTfVolumeSplit(i);
                buyVol += split.buyVol;
                sellVol += split.sellVol;
            }
            double total = buyVol + sellVol;
            return total > 0 ? (buyVol / total) * 100.0 : 50.0;
        }

        private int ClassifyConfidenceCode(double buyPct)
        {
            double upperBound = 50.0 + ConfidenceNeutralBandPct / 2.0;
            double lowerBound = 50.0 - ConfidenceNeutralBandPct / 2.0;
            return buyPct >= upperBound ? 1 : (buyPct <= lowerBound ? -1 : 0);
        }

        // Splits a chart bar's volume into buy/sell using the lower-timeframe
        // candles inside it (each classified by its own close vs. open).
        // Falls back to classifying the chart bar itself when no lower-
        // timeframe candles are available for it.
        private (double buyVol, double sellVol) GetConfLowerTfVolumeSplit(int index)
        {
            double buyVol = 0.0;
            double sellVol = 0.0;
            int count = 0;

            if (_confLowerBars != null)
            {
                DateTime openTime = Bars.OpenTimes[index];
                DateTime upperBound = index < Bars.Count - 1 ? Bars.OpenTimes[index + 1] : DateTime.MaxValue;

                int startIdx = LowerBoundByTime(_confLowerBars.OpenTimes, openTime);
                for (int i = startIdx; i < _confLowerBars.Count; i++)
                {
                    DateTime t = _confLowerBars.OpenTimes[i];
                    if (t < openTime) continue;
                    if (t >= upperBound) break;

                    double c = _confLowerBars.ClosePrices[i];
                    double o = _confLowerBars.OpenPrices[i];
                    double v = _confLowerBars.TickVolumes[i];
                    count++;

                    if (c > o) buyVol += v;
                    else if (c < o) sellVol += v;
                    else { buyVol += v / 2.0; sellVol += v / 2.0; }
                }
            }

            if (count == 0)
            {
                double c = Bars.ClosePrices[index];
                double o = Bars.OpenPrices[index];
                double v = Bars.TickVolumes[index];

                if (c > o) { buyVol = v; sellVol = 0.0; }
                else if (c < o) { buyVol = 0.0; sellVol = v; }
                else { buyVol = v / 2.0; sellVol = v / 2.0; }
            }

            return (buyVol, sellVol);
        }

        // First index whose time is >= target, in an ascending TimeSeries.
        private int LowerBoundByTime(TimeSeries series, DateTime target)
        {
            int lo = 0, hi = series.Count - 1, result = series.Count;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (series[mid] >= target) { result = mid; hi = mid - 1; }
                else lo = mid + 1;
            }
            return result;
        }

        // Picks a timeframe several times smaller than the chart, never
        // dropping below 1 minute. Returns null on a 1-minute-or-faster
        // chart, meaning "no lower timeframe" - those bars use the same-bar
        // classification fallback in GetConfLowerTfVolumeSplit.
        private TimeFrame GetAutoConfidenceLowerTimeframe()
        {
            double chartSeconds = GetTimeframeMinutes(TimeFrame) * 60.0;

            if (chartSeconds <= 60) return null;
            if (chartSeconds <= 900) return TimeFrame.Minute;
            if (chartSeconds <= 3600) return TimeFrame.Minute5;
            if (chartSeconds <= 14400) return TimeFrame.Minute15;
            if (chartSeconds <= 43200) return TimeFrame.Hour;
            if (chartSeconds <= 86400) return TimeFrame.Hour4;
            return TimeFrame.Daily;
        }

        // cAlgo's TimeFrame doesn't expose its length directly; its ToString()
        // gives names like "Minute", "Minute5", "Hour4", "Daily" - parsed here
        // rather than hardcoding every enum member, so custom/less-common
        // timeframes still resolve sensibly.
        private double GetTimeframeMinutes(TimeFrame tf)
        {
            string name = tf.ToString();

            if (name.StartsWith("Minute"))
            {
                string digits = name.Substring("Minute".Length);
                return digits.Length == 0 ? 1.0 : double.Parse(digits);
            }
            if (name.StartsWith("Hour"))
            {
                string digits = name.Substring("Hour".Length);
                return (digits.Length == 0 ? 1.0 : double.Parse(digits)) * 60.0;
            }
            if (name.StartsWith("Daily"))
            {
                return 1440.0;
            }
            if (name.StartsWith("Day"))
            {
                string digits = name.Substring("Day".Length);
                return (digits.Length == 0 ? 1.0 : double.Parse(digits)) * 1440.0;
            }
            if (name.StartsWith("Weekly") || name.StartsWith("Week"))
            {
                return 10080.0;
            }
            if (name.StartsWith("Monthly") || name.StartsWith("Month"))
            {
                return 43200.0;
            }

            // Unknown/tick-based timeframe: treat as very fast so we skip
            // intrabar splitting and fall back to same-bar classification.
            return 0.0;
        }
    }
}
