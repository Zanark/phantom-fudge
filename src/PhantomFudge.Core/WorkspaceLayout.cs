namespace PhantomFudge.Core;

public readonly record struct WorkspaceBounds(
    double Left, double Top, double Width, double Height, double MinimumWidth, double MinimumHeight);

public static class WorkspaceLayout
{
    public const double DesiredWidth = 1240;
    public const double DesiredHeight = 1520;
    public const double MinimumWidth = 470;
    public const double MinimumHeight = 560;
    public const double ReadingColumnsThreshold = 900;
    public const double EdgeInset = 12;

    public static bool UseReadingColumns(double width) => width >= ReadingColumnsThreshold;

    public static WorkspaceBounds FitToWorkArea(
        double workLeft, double workTop, double workWidth, double workHeight,
        double requestedWidth = DesiredWidth, double requestedHeight = DesiredHeight,
        double? requestedLeft = null, double? requestedTop = null)
    {
        if (!double.IsFinite(workLeft) || !double.IsFinite(workTop) ||
            !double.IsFinite(workWidth) || !double.IsFinite(workHeight) ||
            workWidth <= 0 || workHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(workWidth), "A finite, positive work area is required.");

        var insetX = Math.Min(EdgeInset, workWidth / 4);
        var insetY = Math.Min(EdgeInset, workHeight / 4);
        var availableWidth = workWidth - insetX * 2;
        var availableHeight = workHeight - insetY * 2;
        var minimumWidth = Math.Min(MinimumWidth, availableWidth);
        var minimumHeight = Math.Min(MinimumHeight, availableHeight);
        var width = Math.Clamp(double.IsFinite(requestedWidth) ? requestedWidth : DesiredWidth,
            minimumWidth, availableWidth);
        var height = Math.Clamp(double.IsFinite(requestedHeight) ? requestedHeight : DesiredHeight,
            minimumHeight, availableHeight);
        var left = requestedLeft is { } x && double.IsFinite(x) ? x : workLeft + (workWidth - width) / 2;
        var top = requestedTop is { } y && double.IsFinite(y) ? y : workTop + (workHeight - height) / 2;
        return new(
            Math.Clamp(left, workLeft + insetX, workLeft + workWidth - insetX - width),
            Math.Clamp(top, workTop + insetY, workTop + workHeight - insetY - height),
            width, height, minimumWidth, minimumHeight);
    }
}
