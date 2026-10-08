namespace PhantomFudge.Core;

public enum UiTheme { DeepSeaFoam, HarborDaylight, Plain }

public sealed record ThemePalette(
    string Name, string Background, string Surface, string Text, string Muted,
    string Heading, string Border, string Accent, string Hover, string Selection,
    string SelectionText, string ActionText, string Input, string Warning, string Error)
{
    public static ThemePalette For(UiTheme theme) => theme switch
    {
        UiTheme.DeepSeaFoam => new(
            "DeepSeaFoam", "#000F13", "#001E26", "#93A1A1", "#839496",
            "#EEE8D5", "#586E75", "#00A591", "#122E36", "#003748",
            "#EEE8D5", "#000F13", "#001E26", "#EBE565", "#E84A5F"),
        UiTheme.HarborDaylight => new(
            "Harbor Daylight", "#F3F2E9", "#E3ECE7", "#355451", "#536B66",
            "#173D3A", "#6B857E", "#006F63", "#D7E1DC", "#C7DDD7",
            "#173D3A", "#FCFAF2", "#FCFAF2", "#77600E", "#AD3E55"),
        UiTheme.Plain => new(
            "Plain B/W", "#FAF9F6", "#F0F0EC", "#202B30", "#536166",
            "#202B30", "#879A99", "#202B30", "#DFE3E2", "#D7E1DC",
            "#202B30", "#FFFFFF", "#FFFFFF", "#685837", "#9C2636"),
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    public static UiTheme NextThemed(UiTheme current) =>
        current == UiTheme.DeepSeaFoam ? UiTheme.HarborDaylight : UiTheme.DeepSeaFoam;
}
