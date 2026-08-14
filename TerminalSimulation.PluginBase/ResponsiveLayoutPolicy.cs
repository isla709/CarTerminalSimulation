namespace TerminalSimulation.PluginBase;

public enum ResponsiveLayoutMode
{
    Compact,
    Standard,
    Wide
}

public static class ResponsiveLayoutPolicy
{
    public const double CompactBreakpoint = 760;
    public const double WideBreakpoint = 1120;

    public static ResponsiveLayoutMode GetMode(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return ResponsiveLayoutMode.Standard;
        }

        if (width < CompactBreakpoint)
        {
            return ResponsiveLayoutMode.Compact;
        }

        return width < WideBreakpoint
            ? ResponsiveLayoutMode.Standard
            : ResponsiveLayoutMode.Wide;
    }

    public static double GetMainControlColumnWidth(double windowWidth) =>
        GetMode(windowWidth) == ResponsiveLayoutMode.Wide ? 400 : 340;
}
