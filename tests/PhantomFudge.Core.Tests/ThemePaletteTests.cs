namespace PhantomFudge.Core.Tests;

public sealed class ThemePaletteTests
{
    [Theory]
    [InlineData(UiTheme.DeepSeaFoam, "#000F13", "#001E26", "#93A1A1", "#00A591")]
    [InlineData(UiTheme.HarborDaylight, "#F3F2E9", "#E3ECE7", "#355451", "#006F63")]
    [InlineData(UiTheme.Plain, "#FAF9F6", "#F0F0EC", "#202B30", "#202B30")]
    public void CanonicalRoleColorsAreStable(UiTheme theme, string background, string surface,
        string text, string accent)
    {
        var palette = ThemePalette.For(theme);
        Assert.Equal(background, palette.Background);
        Assert.Equal(surface, palette.Surface);
        Assert.Equal(text, palette.Text);
        Assert.Equal(accent, palette.Accent);
    }

    [Fact]
    public void ThemeToggleAlternatesCompanionsAndExitsPlain()
    {
        Assert.Equal(UiTheme.HarborDaylight, ThemePalette.NextThemed(UiTheme.DeepSeaFoam));
        Assert.Equal(UiTheme.DeepSeaFoam, ThemePalette.NextThemed(UiTheme.HarborDaylight));
        Assert.Equal(UiTheme.DeepSeaFoam, ThemePalette.NextThemed(UiTheme.Plain));
    }

    [Fact]
    public void InvalidPaletteDoesNotSilentlyBecomePlain() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ThemePalette.For((UiTheme)99));

    [Theory]
    [InlineData(UiTheme.DeepSeaFoam)]
    [InlineData(UiTheme.HarborDaylight)]
    [InlineData(UiTheme.Plain)]
    public void OpaqueReadingAndSelectionPairsHaveSufficientContrast(UiTheme theme)
    {
        var palette = ThemePalette.For(theme);
        Assert.True(Contrast(palette.Text, palette.Background) >= 4.5);
        Assert.True(Contrast(palette.SelectionText, palette.Selection) >= 4.5);
        Assert.True(Contrast(palette.ActionText, palette.Accent) >= 4.5);
    }

    private static double Contrast(string first, string second)
    {
        static double Luminance(string hex)
        {
            var channels = Convert.FromHexString(hex[1..]);
            var linear = channels.Select(value =>
            {
                var s = value / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }).ToArray();
            return linear[0] * 0.2126 + linear[1] * 0.7152 + linear[2] * 0.0722;
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
}
