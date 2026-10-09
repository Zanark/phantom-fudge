using System.Windows;
using System.Windows.Media;
using PhantomFudge.Core;

namespace PhantomFudge.App;

internal static class ThemeBrushes
{
    public static void Apply(ResourceDictionary resources, UiTheme theme)
    {
        var palette = ThemePalette.For(theme);
        var colors = new Dictionary<string, string>
        {
            ["ThemeBackground"] = palette.Background, ["ThemeSurface"] = palette.Surface,
            ["ThemeText"] = palette.Text, ["ThemeMuted"] = palette.Muted,
            ["ThemeHeading"] = palette.Heading, ["ThemeBorder"] = palette.Border,
            ["ThemeAccent"] = palette.Accent, ["ThemeHover"] = palette.Hover,
            ["ThemeSelection"] = palette.Selection, ["ThemeSelectionText"] = palette.SelectionText,
            ["ThemeActionText"] = palette.ActionText, ["ThemeInput"] = palette.Input,
            ["ThemeWarning"] = palette.Warning, ["ThemeError"] = palette.Error,
            ["ThemeHighContrastReading"] = palette.HighContrastReading
        };
        foreach (var (key, value) in colors)
            resources[key] = Brush(value);

        resources[SystemColors.ControlBrushKey] = resources["ThemeSurface"];
        resources[SystemColors.ControlTextBrushKey] = resources["ThemeText"];
        resources[SystemColors.WindowBrushKey] = resources["ThemeInput"];
        resources[SystemColors.WindowTextBrushKey] = resources["ThemeText"];
        resources[SystemColors.HighlightBrushKey] = resources["ThemeSelection"];
        resources[SystemColors.HighlightTextBrushKey] = resources["ThemeSelectionText"];
        resources[SystemColors.GrayTextBrushKey] = resources["ThemeMuted"];
    }

    public static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);

    private static SolidColorBrush Brush(string value)
    {
        var brush = new SolidColorBrush(Color(value));
        brush.Freeze();
        return brush;
    }
}
