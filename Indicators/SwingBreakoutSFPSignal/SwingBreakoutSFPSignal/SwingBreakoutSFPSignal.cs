using System;
using System.Collections.Generic;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo
{
    // =========================================================================
    // SwingBreakoutSFPSignal - pure swing-pivot A-B-C-D-E fib-extension entry
    // signal indicator. See STRATEGY.md for the full trading method this
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
    // A fresh setup starts directly from a confirmed swing pivot - no level
    // reaction, break, or retest is required first:
    //   A = a confirmed swing high (bearish setup) or swing low (bullish
    //       setup), found by the same left/right fractal pivot test used
    //       throughout - this is what makes the whole sequence automatically
    //       scale to small or large swings: a pivot is "a bar more extreme
    //       than its N neighbors on each side", a shape test with no fixed
    //       price/ATR distance involved, so a 5-pip swing and a 500-pip swing
    //       are found the same way.
    //   B = the next confirmed swing pivot beyond A (first leg away from A).
    //   C = the next confirmed swing pivot back beyond B (first retracement),
    //       required to retrace at least 50% of the A-B leg.
    //   Wait for a close past the 100% extension of A-B-C (= C + (B-A)).
    //   D = the first confirmed swing pivot after that extension break. It
    //       must form within MaxBarsFromExtensionBreakToD bars.
    //   E = the 50% retracement of D-C - a limit order is placed there
    //       rather than entering at market.
    //   Invalidated at any point (after A) if price closes back through A.
    //
    // A fresh pivot takes an idle setup slot, preserving other valid
    // in-progress attempts in the same direction (up to three at once).
    //
    // OUTPUTS (the Robot's entire interface into this indicator):
    //   - BullishSignal / BearishSignal: 1 when D confirms and the 50% D-C
    //     retracement order can be placed, 0 otherwise.
    //   - StopAnchor: on a signal bar, C (StopLossMode.Conservative) or A
    //     (StopLossMode.Normal). NaN otherwise.
    //   - EntryLevel: on a signal bar, the 50% retracement of D-C (E). NaN
    //     otherwise.
    //   - TargetLevel: on a signal bar, the 261.8% expansion of A->B->C:
    //     C + (B - A) x 2.618. NaN otherwise. This single formula
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

        [Parameter("ATR Period", DefaultValue = SharedSignalDefaults.AtrPeriod, MinValue = 1, Group = "Signal")]
        public int AtrPeriod { get; set; }

        // Conservative -> SL at C (tighter, inside the second retracement).
        // Normal -> SL at A (wider, the original swing extreme). See
        // STRATEGY.md.
        [Parameter("Stop-Loss Mode", DefaultValue = SharedSignalDefaults.StopMode, Group = "Signal")]
        public StopLossMode StopMode { get; set; }

        [Parameter("Show A-B-C-D Structure", DefaultValue = true, Group = "Signal")]
        public bool ShowAbcdStructure { get; set; }

        [Parameter("A-B-C Projection Width (bars)", DefaultValue = 20, MinValue = 5, MaxValue = 500, Group = "Signal")]
        public int AbcdProjectionBars { get; set; }

        [Parameter("Max. Bars from 100% Break to D", DefaultValue = 80, MinValue = 1, MaxValue = 100, Group = "Signal")]
        public int MaxBarsFromExtensionBreakToD { get; set; }
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

        private const int StructureLineThickness = 1;
        private static readonly double[] AbcdExpansionRatios = { 0.0, 0.5, 1.0, 1.618, 2.0, 2.618, 3.618 };

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
        private static readonly Color AbcdBullColor = Color.FromArgb(255, 0, 140, 90);
        private static readonly Color AbcdBearColor = Color.FromArgb(255, 180, 35, 35);

        [Output("Bullish Signal", LineColor = "Transparent")]
        public IndicatorDataSeries BullishSignal { get; set; }

        [Output("Bearish Signal", LineColor = "Transparent")]
        public IndicatorDataSeries BearishSignal { get; set; }

        [Output("Stop Anchor", LineColor = "Transparent")]
        public IndicatorDataSeries StopAnchor { get; set; }

        [Output("Target Level", LineColor = "Transparent")]
        public IndicatorDataSeries TargetLevel { get; set; }

        [Output("Entry Level", LineColor = "Transparent")]
        public IndicatorDataSeries EntryLevel { get; set; }

        [Output("Order Cancellation Level", LineColor = "Transparent")]
        public IndicatorDataSeries OrderCancellationLevel { get; set; }

        [Output("Confidence Buy", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceBuySignal { get; set; }

        [Output("Confidence Sell", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceSellSignal { get; set; }

        [Output("Confidence Stop", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceStopAnchor { get; set; }

        [Output("Confidence Target", LineColor = "Transparent")]
        public IndicatorDataSeries ConfidenceTargetLevel { get; set; }

        private AverageTrueRange _atr;

        // A-B-C-D state is retained independently for up to three concurrent
        // setups per direction. A new pivot takes an idle slot and never
        // resets a valid older setup.
        private const int MaxActiveSetupsPerDirection = 3;
        private const int SequenceSlotCount = MaxActiveSetupsPerDirection * 2;
        private IndicatorDataSeries[] _seqState = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqPhaseStartBar = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqA = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqB = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqC = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqD = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqABar = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqBBar = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqCBar = new IndicatorDataSeries[SequenceSlotCount];
        private IndicatorDataSeries[] _seqDBar = new IndicatorDataSeries[SequenceSlotCount];

        private const double StIdle = 0;
        private const double StSeekingB = 1;
        private const double StSeekingC = 2;
        private const double StSeekingExt = 3;
        private const double StSeekingD = 4;

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

            for (int i = 0; i < SequenceSlotCount; i++)
            {
                _seqState[i] = CreateDataSeries();
                _seqPhaseStartBar[i] = CreateDataSeries();
                _seqA[i] = CreateDataSeries();
                _seqB[i] = CreateDataSeries();
                _seqC[i] = CreateDataSeries();
                _seqD[i] = CreateDataSeries();
                _seqABar[i] = CreateDataSeries();
                _seqBBar[i] = CreateDataSeries();
                _seqCBar[i] = CreateDataSeries();
                _seqDBar[i] = CreateDataSeries();
            }

            var confLowerTf = GetAutoConfidenceLowerTimeframe();
            _confLowerBars = confLowerTf != null ? MarketData.GetBars(confLowerTf) : null;

            _lastMajorDotCode = CreateDataSeries();
            _lastMinorDotCode = CreateDataSeries();

            RemoveStaleAbcdObjects();
            Chart.MouseMove += OnChartMouseMove;
        }

        private void RemoveStaleAbcdObjects()
        {
            var names = new List<string>();
            foreach (var chartObject in Chart.Objects)
                if (chartObject.Name.StartsWith("ABCD_"))
                    names.Add(chartObject.Name);

            foreach (var name in names)
                Chart.RemoveObject(name);
        }

        public override void Calculate(int index)
        {
            BullishSignal[index] = 0;
            BearishSignal[index] = 0;
            StopAnchor[index] = double.NaN;
            TargetLevel[index] = double.NaN;
            EntryLevel[index] = double.NaN;
            OrderCancellationLevel[index] = double.NaN;
            ConfidenceBuySignal[index] = 0;
            ConfidenceSellSignal[index] = 0;
            ConfidenceStopAnchor[index] = double.NaN;
            ConfidenceTargetLevel[index] = double.NaN;

            double barHigh = Bars.HighPrices[index];
            double barLow = Bars.LowPrices[index];
            double barClose = Bars.ClosePrices[index];
            double atrVal = _atr.Result[index];

            // A fresh confirmed MAJOR pivot is the ONLY trigger now: a swing
            // low starts a bullish A-B-C-D-E attempt, a swing high starts a
            // bearish one. No level reaction, break, or retest required -
            // see the class header.
            //
            // A uses the Major lookback (bigger left/right window, shared
            // with Confidence Dots) rather than the minor one - a 5/5
            // fractal test fires constantly on noise-sized wiggles, which
            // would spawn a fresh conflicting setup on almost every ranging
            // stretch. The Major window is still a pure bar-count shape
            // test with no fixed price/ATR distance, so it stays scale-
            // relative - it just requires more neighboring bars to agree,
            // which naturally filters out minor/noise pivots while still
            // finding big and small MAJOR swings the same way. B, C, and D
            // still use the minor lookback (SwingLeftBars/SwingRightBars)
            // once a setup is underway, to find the actual fine-grained
            // retracement structure inside the major swing.
            int candidate = index - SwingRightBars;
            int majorCandidate = index - MajorSwingRightBars;
            bool pivotLowAtMajorCandidate = majorCandidate >= 0 && IsPivotLow(majorCandidate, MajorSwingLeftBars, MajorSwingRightBars);
            bool pivotHighAtMajorCandidate = majorCandidate >= 0 && IsPivotHigh(majorCandidate, MajorSwingLeftBars, MajorSwingRightBars);

            ProcessSequences(0, 1, index, candidate, majorCandidate, pivotLowAtMajorCandidate, barHigh, barLow, barClose, BullishSignal);
            ProcessSequences(1, -1, index, candidate, majorCandidate, pivotHighAtMajorCandidate, barHigh, barLow, barClose, BearishSignal);

            if (BearishSignal[index] > 0.5)
                Chart.DrawIcon("SFPBear" + index, ChartIconType.Circle, Bars.OpenTimes[index], barHigh + atrVal * 0.3, BearColor);
            if (BullishSignal[index] > 0.5)
                Chart.DrawIcon("SFPBull" + index, ChartIconType.Circle, Bars.OpenTimes[index], barLow - atrVal * 0.3, BullColor);

            UpdateConfidenceDots(index);
        }

        private void ProcessSequences(int dirIdx, int dir, int index, int candidate, int majorCandidate, bool triggered,
            double barHigh, double barLow, double barClose, IndicatorDataSeries signalSeries)
        {
            int startSlot = dirIdx * MaxActiveSetupsPerDirection;
            int triggeredSlot = -1;

            if (triggered)
            {
                for (int slot = startSlot; slot < startSlot + MaxActiveSetupsPerDirection; slot++)
                {
                    double state = index > 0 ? _seqState[slot][index - 1] : StIdle;
                    if (state == StIdle)
                    {
                        triggeredSlot = slot;
                        break;
                    }
                }
            }

            for (int slot = startSlot; slot < startSlot + MaxActiveSetupsPerDirection; slot++)
                ProcessSequence(slot, dir, index, candidate, majorCandidate, slot == triggeredSlot, barHigh, barLow, barClose, signalSeries);
        }

        // The A-B-C-D state machine for one independently tracked setup.
        // A new MAJOR pivot is assigned an idle slot by ProcessSequences;
        // this method advances that one setup independently. See
        // STRATEGY.md for the full sequence and the diagram of every step.
        private void ProcessSequence(int dirIdx, int dir, int index, int candidate, int majorCandidate, bool triggered,
            double barHigh, double barLow, double barClose, IndicatorDataSeries signalSeries)
        {
            double state = index > 0 ? _seqState[dirIdx][index - 1] : StIdle;
            double phaseStartBar = index > 0 ? _seqPhaseStartBar[dirIdx][index - 1] : double.NaN;
            double a = index > 0 ? _seqA[dirIdx][index - 1] : double.NaN;
            double b = index > 0 ? _seqB[dirIdx][index - 1] : double.NaN;
            double c = index > 0 ? _seqC[dirIdx][index - 1] : double.NaN;
            double d = index > 0 ? _seqD[dirIdx][index - 1] : double.NaN;
            double aBar = index > 0 ? _seqABar[dirIdx][index - 1] : double.NaN;
            double bBar = index > 0 ? _seqBBar[dirIdx][index - 1] : double.NaN;
            double cBar = index > 0 ? _seqCBar[dirIdx][index - 1] : double.NaN;
            double dBar = index > 0 ? _seqDBar[dirIdx][index - 1] : double.NaN;

            if (triggered)
            {
                // A is the MAJOR pivot bar itself (majorCandidate), not the
                // current bar - confirmation of the pivot already happened
                // over the Major left/right lookback window. B/C/D still
                // search using the minor lookback (candidate) from here on.
                state = StSeekingB;
                phaseStartBar = majorCandidate;
                a = dir == 1 ? Bars.LowPrices[majorCandidate] : Bars.HighPrices[majorCandidate];
                b = double.NaN;
                c = double.NaN;
                d = double.NaN;
                aBar = majorCandidate;
                bBar = double.NaN;
                cBar = double.NaN;
                dBar = double.NaN;
            }
            else if (state != StIdle)
            {
                bool brokeA = dir == 1 ? barClose < a : barClose > a;

                if (brokeA)
                {
                    // Structure invalidated - price closed back through the
                    // original A before an entry ever fired. Abandon.
                    state = StIdle;
                    phaseStartBar = double.NaN;
                    a = double.NaN; b = double.NaN; c = double.NaN; d = double.NaN;
                    aBar = double.NaN; bBar = double.NaN; cBar = double.NaN; dBar = double.NaN;
                }
                else if (state == StSeekingB)
                {
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
                                bBar = candidate;
                                state = StSeekingC;
                                phaseStartBar = candidate;
                            }
                        }
                    }
                }
                else if (state == StSeekingC)
                {
                    if (candidate > phaseStartBar)
                    {
                        bool isPivotC = dir == 1 ? IsPivotLow(candidate, SwingLeftBars, SwingRightBars) : IsPivotHigh(candidate, SwingLeftBars, SwingRightBars);
                        if (isPivotC)
                        {
                            double candidatePrice = dir == 1 ? Bars.LowPrices[candidate] : Bars.HighPrices[candidate];
                            double retracementMidpoint = (a + b) / 2.0;
                            bool isValidRetracement = dir == 1
                                ? (candidatePrice < b && candidatePrice > a && candidatePrice <= retracementMidpoint)
                                : (candidatePrice > b && candidatePrice < a && candidatePrice >= retracementMidpoint);
                            if (isValidRetracement)
                            {
                                c = candidatePrice;
                                cBar = candidate;
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
                        d = dir == 1 ? barHigh : barLow;
                        dBar = index;
                    }
                }
                else if (state == StSeekingD)
                {
                    if (candidate - phaseStartBar > MaxBarsFromExtensionBreakToD)
                    {
                        state = StIdle;
                        phaseStartBar = double.NaN;
                        a = double.NaN; b = double.NaN; c = double.NaN; d = double.NaN;
                        aBar = double.NaN; bBar = double.NaN; cBar = double.NaN; dBar = double.NaN;
                    }
                    else if (candidate > phaseStartBar)
                    {
                        bool retracementConfirmed = dir == 1
                            ? IsPivotLow(candidate, SwingLeftBars, SwingRightBars)
                            : IsPivotHigh(candidate, SwingLeftBars, SwingRightBars);
                        if (retracementConfirmed)
                        {
                            FindPostBreakExtreme((int)phaseStartBar, candidate, dir, out dBar, out d);
                            double entryPrice = (d + c) / 2.0;
                            if (HasForwardStructure(aBar, bBar, cBar, dBar))
                            {
                                signalSeries[index] = 1;
                                EntryLevel[index] = entryPrice;
                                OrderCancellationLevel[index] = d;
                                StopAnchor[index] = StopMode == StopLossMode.Conservative ? c : a;
                                TargetLevel[index] = c + (b - a) * TargetExtensionRatio;
                                DrawAbcdStructure(index, dir, (int)aBar, a, (int)bBar, b, (int)cBar, c, (int)dBar, d);
                            }

                            state = StIdle;
                            phaseStartBar = double.NaN;
                            a = double.NaN; b = double.NaN; c = double.NaN; d = double.NaN;
                            aBar = double.NaN; bBar = double.NaN; cBar = double.NaN; dBar = double.NaN;
                        }
                    }
                }
            }

            _seqState[dirIdx][index] = state;
            _seqPhaseStartBar[dirIdx][index] = phaseStartBar;
            _seqA[dirIdx][index] = a;
            _seqB[dirIdx][index] = b;
            _seqC[dirIdx][index] = c;
            _seqD[dirIdx][index] = d;
            _seqABar[dirIdx][index] = aBar;
            _seqBBar[dirIdx][index] = bBar;
            _seqCBar[dirIdx][index] = cBar;
            _seqDBar[dirIdx][index] = dBar;
        }

        private void FindPostBreakExtreme(int startBar, int endBar, int dir, out double extremeBar, out double extremePrice)
        {
            int selectedBar = startBar;
            double selectedPrice = dir == 1 ? Bars.HighPrices[startBar] : Bars.LowPrices[startBar];

            for (int bar = startBar + 1; bar <= endBar; bar++)
            {
                double price = dir == 1 ? Bars.HighPrices[bar] : Bars.LowPrices[bar];
                if (dir == 1 ? price > selectedPrice : price < selectedPrice)
                {
                    selectedPrice = price;
                    selectedBar = bar;
                }
            }

            extremeBar = selectedBar;
            extremePrice = selectedPrice;
        }

        private void DrawAbcdStructure(int signalIndex, int dir, int aBar, double a, int bBar, double b, int cBar, double c, int dBar, double d)
        {
            if (!ShowAbcdStructure || !HasForwardStructure(aBar, bBar, cBar, dBar))
                return;

            string name = $"ABCD_{(dir == 1 ? "Bull" : "Bear")}_{signalIndex}";
            Color color = dir == 1 ? AbcdBullColor : AbcdBearColor;

            // Remove chart objects drawn by the earlier manual ABCD renderer.
            Chart.RemoveObject(name + "_AB");
            Chart.RemoveObject(name + "_BC");
            Chart.RemoveObject(name + "_CD");
            Chart.RemoveObject(name + "_A");
            Chart.RemoveObject(name + "_B");
            Chart.RemoveObject(name + "_C");
            Chart.RemoveObject(name + "_D");
            Chart.RemoveObject(name + "_E");
            Chart.RemoveObject(name + "_Entry");
            Chart.RemoveObject(name);
            Chart.RemoveObject(name + "_DC");

            int expansionEndBar = Math.Min(Bars.Count - 1, cBar + AbcdProjectionBars);
            int entryEndBar = Math.Min(Bars.Count - 1, dBar + AbcdProjectionBars);
            Chart.DrawTrendLine(name + "_AB", Bars.OpenTimes[aBar], a, Bars.OpenTimes[bBar], b, color, StructureLineThickness, LineStyle.Solid);
            Chart.DrawTrendLine(name + "_BC", Bars.OpenTimes[bBar], b, Bars.OpenTimes[cBar], c, color, StructureLineThickness, LineStyle.Solid);
            for (int i = 0; i < AbcdExpansionRatios.Length; i++)
            {
                double level = c + (b - a) * AbcdExpansionRatios[i];
                Chart.DrawTrendLine(name + "_Level" + i, Bars.OpenTimes[cBar], level, Bars.OpenTimes[expansionEndBar], level, color, StructureLineThickness, LineStyle.DotsRare);
            }

            double entryLevel = (d + c) / 2.0;
            Chart.DrawTrendLine(name + "_Entry", Bars.OpenTimes[dBar], entryLevel, Bars.OpenTimes[entryEndBar], entryLevel, color, StructureLineThickness, LineStyle.Solid);
            Chart.DrawText(name + "_A", "A", Bars.OpenTimes[aBar], a, color);
            Chart.DrawText(name + "_B", "B", Bars.OpenTimes[bBar], b, color);
            Chart.DrawText(name + "_C", "C", Bars.OpenTimes[cBar], c, color);
            Chart.DrawText(name + "_D", "D", Bars.OpenTimes[dBar], d, color);
            Chart.DrawText(name + "_E", "E (50% entry)", Bars.OpenTimes[dBar], entryLevel, color);
        }

        private static bool HasForwardStructure(double aBar, double bBar, double cBar, double dBar)
        {
            return !double.IsNaN(aBar) && !double.IsNaN(bBar) && !double.IsNaN(cBar) && !double.IsNaN(dBar)
                && aBar < bBar && bBar < cBar && cBar < dBar;
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
