namespace cAlgo
{
    // Default values for the "Shared Signal Config" parameters that
    // SwingBreakoutSFPSignal.cs and Swingbreakouttrader.cs both declare.
    // Both files' [Parameter(..., DefaultValue = SharedSignalDefaults.X)]
    // attributes read from here, so changing a default only requires an
    // edit in one place.
    //
    // This does NOT keep the two files' parameter ORDER or NAMES in sync -
    // that's still a manual, positional contract (see the header comments
    // in both files). It only keeps the DEFAULT VALUES identical.
    public static class SharedSignalDefaults
    {
        public const int SwingLeftBars = 5;
        public const int SwingRightBars = 5;
        public const int AtrPeriod = 5;
        public const StopLossMode StopMode = StopLossMode.Normal;
    }
}
