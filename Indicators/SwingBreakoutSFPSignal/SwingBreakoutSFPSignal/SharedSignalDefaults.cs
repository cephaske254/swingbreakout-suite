namespace cAlgo
{
    // Default values for the 10 "Shared Signal Config" parameters that
    // SwingBreakoutSFPSignal.cs and Swingbreakouttrader.cs both declare.
    // Both files' [Parameter(..., DefaultValue = SharedSignalDefaults.X)]
    // attributes read from here, so changing a default only requires an
    // edit in one place.
    //
    // This does NOT keep the two files' parameter ORDER or NAMES in sync -
    // that's still a manual, positional contract (see the header comments
    // in both files). It only keeps the DEFAULT VALUES identical.
    //
    // TrendTimeFrame is a TimeFrame (not a primitive), and cAlgo attribute
    // arguments must be compile-time constants, so its default is carried
    // here as the string cAlgo itself expects ("Hour4", etc.) rather than
    // as a TimeFrame instance.
    public static class SharedSignalDefaults
    {
        public const int SwingLeftBars = 5;
        public const int SwingRightBars = 5;
        public const bool TrackPriorSwing = true;
        public const bool TrackPDHPDL = true;
        public const bool TrackHcomLcom = true;
        public const string TrendTimeFrame = "Minute5";
        public const bool RequireTrendFilter = true;
        public const int AtrPeriod = 5;
        public const double MinSweepDepthATRmult = 0.10;
        public const StopLossMode StopMode = StopLossMode.Normal;
    }
}
