namespace PhantomFudge.Core.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void DesiredWindowDoublesBothOriginalDimensions()
    {
        Assert.Equal(620 * 2, WorkspaceLayout.DesiredWidth);
        Assert.Equal(760 * 2, WorkspaceLayout.DesiredHeight);
        var bounds = WorkspaceLayout.FitToWorkArea(0, 0, 2560, 1800);
        Assert.Equal(1240, bounds.Width);
        Assert.Equal(1520, bounds.Height);
        Assert.Equal(470, bounds.MinimumWidth);
        Assert.Equal(560, bounds.MinimumHeight);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1040)]
    [InlineData(0, 0, 1366, 728)]
    [InlineData(-1280, -160, 1280, 680)]
    [InlineData(0, 0, 800, 600)]
    [InlineData(0, 0, 460, 520)]
    public void WindowAndMinimumStayInsideWorkArea(double left, double top, double width, double height)
    {
        var bounds = WorkspaceLayout.FitToWorkArea(left, top, width, height);
        Assert.InRange(bounds.Left, left, left + width - bounds.Width);
        Assert.InRange(bounds.Top, top, top + height - bounds.Height);
        Assert.InRange(bounds.Width, bounds.MinimumWidth, width);
        Assert.InRange(bounds.Height, bounds.MinimumHeight, height);
        Assert.True(bounds.Width <= WorkspaceLayout.DesiredWidth);
        Assert.True(bounds.Height <= WorkspaceLayout.DesiredHeight);
    }

    [Fact]
    public void RevealPreservesUserSizeAndVisiblePosition()
    {
        var bounds = WorkspaceLayout.FitToWorkArea(0, 0, 1920, 1040, 720, 620, 100, 150);
        Assert.Equal(720, bounds.Width);
        Assert.Equal(620, bounds.Height);
        Assert.Equal(100, bounds.Left);
        Assert.Equal(150, bounds.Top);
    }

    [Fact]
    public void RecoveryMovesAnOffscreenWindowWithoutIncreasingItsSize()
    {
        var bounds = WorkspaceLayout.FitToWorkArea(-1280, 0, 1280, 720, 600, 600, 9000, -1000);
        Assert.Equal(600, bounds.Width);
        Assert.Equal(600, bounds.Height);
        Assert.Equal(-612, bounds.Left);
        Assert.Equal(12, bounds.Top);
    }

    [Theory]
    [InlineData(470, false)]
    [InlineData(899, false)]
    [InlineData(900, true)]
    [InlineData(1240, true)]
    public void NarrowWorkspaceStacksReadingPanes(double width, bool columns) =>
        Assert.Equal(columns, WorkspaceLayout.UseReadingColumns(width));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidWorkAreaIsRejected(double width) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WorkspaceLayout.FitToWorkArea(0, 0, width, 1000));

    [Fact]
    public void UnsetWindowCoordinatesUseCenteredDesiredBounds()
    {
        var bounds = WorkspaceLayout.FitToWorkArea(0, 0, 1920, 1040,
            double.NaN, double.NaN, double.NaN, double.NaN);
        Assert.Equal(340, bounds.Left);
        Assert.Equal(12, bounds.Top);
        Assert.Equal(1240, bounds.Width);
        Assert.Equal(1016, bounds.Height);
    }
}
