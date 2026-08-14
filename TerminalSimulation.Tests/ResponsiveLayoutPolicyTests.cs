using TerminalSimulation.PluginBase;
using Xunit;

namespace TerminalSimulation.Tests;

public sealed class ResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(759, ResponsiveLayoutMode.Compact)]
    [InlineData(760, ResponsiveLayoutMode.Standard)]
    [InlineData(1119, ResponsiveLayoutMode.Standard)]
    [InlineData(1120, ResponsiveLayoutMode.Wide)]
    [InlineData(3840, ResponsiveLayoutMode.Wide)]
    [InlineData(-1, ResponsiveLayoutMode.Standard)]
    [InlineData(double.NaN, ResponsiveLayoutMode.Standard)]
    [InlineData(double.PositiveInfinity, ResponsiveLayoutMode.Standard)]
    public void GetMode_UsesStableBreakpoints(double width, ResponsiveLayoutMode expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetMode(width));
    }

    [Theory]
    [InlineData(900, 340)]
    [InlineData(1119, 340)]
    [InlineData(1120, 400)]
    [InlineData(1920, 400)]
    [InlineData(double.NaN, 340)]
    public void MainControlColumn_LeavesMoreRoomAtCompactWidths(double width, double expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetMainControlColumnWidth(width));
    }
}
