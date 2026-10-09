using System.Globalization;

namespace PhantomFudge.Core.Tests;

public sealed class KeyboardCommandsTests
{
    [Theory]
    [InlineData("start", KeyboardCommandKind.Start, null, null)]
    [InlineData("stop", KeyboardCommandKind.Stop, null, null)]
    [InlineData("record start", KeyboardCommandKind.Start, null, null)]
    [InlineData("record stop", KeyboardCommandKind.Stop, null, null)]
    [InlineData("source mic", KeyboardCommandKind.Source, "mic", null)]
    [InlineData("source computer", KeyboardCommandKind.Source, "computer", null)]
    [InlineData("source both", KeyboardCommandKind.Source, "both", null)]
    [InlineData("devices", KeyboardCommandKind.Devices, null, null)]
    [InlineData("clarify on", KeyboardCommandKind.Clarify, "on", null)]
    [InlineData("clarify off", KeyboardCommandKind.Clarify, "off", null)]
    [InlineData("confirm", KeyboardCommandKind.Confirm, null, null)]
    [InlineData("font 8", KeyboardCommandKind.Font, null, 8)]
    [InlineData("font 14", KeyboardCommandKind.Font, null, 14)]
    [InlineData("font 24", KeyboardCommandKind.Font, null, 24)]
    [InlineData("font 014", KeyboardCommandKind.Font, null, 14)]
    [InlineData("copy question", KeyboardCommandKind.CopyQuestion, null, null)]
    [InlineData("copy answer", KeyboardCommandKind.CopyAnswer, null, null)]
    [InlineData("clear question", KeyboardCommandKind.ClearQuestion, null, null)]
    [InlineData("cancel", KeyboardCommandKind.Cancel, null, null)]
    [InlineData("theme dark", KeyboardCommandKind.Theme, "dark", null)]
    [InlineData("theme light", KeyboardCommandKind.Theme, "light", null)]
    [InlineData("theme plain", KeyboardCommandKind.Theme, "plain", null)]
    [InlineData("opacity 10", KeyboardCommandKind.Opacity, null, 10)]
    [InlineData("opacity 55", KeyboardCommandKind.Opacity, null, 55)]
    [InlineData("opacity 100", KeyboardCommandKind.Opacity, null, 100)]
    [InlineData("opacity 055", KeyboardCommandKind.Opacity, null, 55)]
    [InlineData("ask", KeyboardCommandKind.Ask, null, null)]
    [InlineData("mask", KeyboardCommandKind.Mask, null, null)]
    [InlineData("help", KeyboardCommandKind.Help, null, null)]
    public void ParsesOnlyCanonicalData(string text, KeyboardCommandKind kind, string? argument, int? value)
    {
        var expected = new KeyboardCommand(kind, argument, value);
        Assert.Equal(expected, KeyboardCommands.Parse(text));
        Assert.Equal(expected, KeyboardCommands.Parse($" \t: {text.ToUpperInvariant()} \r\n"));
    }

    [Theory]
    [InlineData(" \t:SoUrCe \r\n MiC \t", "source mic")]
    [InlineData(":  CoPy\tAnSwEr", "copy answer")]
    [InlineData("ReCoRd\nStArT", "start")]
    [InlineData("clarify\r\nOFF", "clarify off")]
    [InlineData("\u2003THEME\u00a0LIGHT\u2003", "theme light")]
    public void NormalizesWhitespaceAndOneLeadingColon(string text, string canonical) =>
        Assert.Equal(KeyboardCommands.Parse(canonical), KeyboardCommands.Parse(text));

    [Fact]
    public void NullInputHasAnAuthoredSafeError()
    {
        var parseError = Assert.Throws<ArgumentNullException>(() => KeyboardCommands.Parse(null!));
        var suggestError = Assert.Throws<ArgumentNullException>(() => KeyboardCommands.Suggest(null!));
        Assert.StartsWith("Enter a built-in command.", parseError.Message);
        Assert.StartsWith("Enter a built-in command.", suggestError.Message);
        Assert.Equal("text", parseError.ParamName);
        Assert.Equal("text", suggestError.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("\u2003\u00a0")]
    [InlineData(":")]
    [InlineData(" \t: \r\n")]
    public void BlankCommandIsRejectedButEmptyPrefixOffersExamples(string text)
    {
        var error = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(text));
        Assert.StartsWith("Enter a built-in command.", error.Message);
        Assert.Equal<string>(KeyboardCommands.Examples.Take(5), KeyboardCommands.Suggest(text));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("sta")]
    [InlineData("restart")]
    [InlineData("shell")]
    [InlineData("run")]
    [InlineData("record")]
    [InlineData("record pause")]
    [InlineData("source")]
    [InlineData("source microphone")]
    [InlineData("source all")]
    [InlineData("source 1")]
    [InlineData("devices mic")]
    [InlineData("clarify")]
    [InlineData("clarify yes")]
    [InlineData("clarify true")]
    [InlineData("confirm yes")]
    [InlineData("font")]
    [InlineData("copy")]
    [InlineData("copy all")]
    [InlineData("clear")]
    [InlineData("clear answer")]
    [InlineData("cancel answer")]
    [InlineData("theme")]
    [InlineData("theme auto")]
    [InlineData("opacity")]
    [InlineData("mask on")]
    [InlineData("ask question")]
    [InlineData("help start")]
    [InlineData("::start")]
    [InlineData(": :start")]
    [InlineData("start:")]
    [InlineData("source :mic")]
    public void RejectsUnknownCommandsMissingArgumentsAndUnknownSubcommands(string text)
    {
        var error = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(text));
        Assert.StartsWith("Unknown command or invalid arguments.", error.Message);
        Assert.Equal("text", error.ParamName);
    }

    [Fact]
    public void EveryExampleRejectsExcessArguments()
    {
        foreach (var example in KeyboardCommands.Examples)
            Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(example + " extra"));
    }

    [Theory]
    [InlineData("7")]
    [InlineData("25")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    public void RejectsFontOutsideInclusiveRange(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse("font " + value));
        Assert.StartsWith("Font size must be a whole number from 8 to 24.", error.Message);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("101")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483647")]
    public void RejectsOpacityOutsideInclusiveRange(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse("opacity " + value));
        Assert.StartsWith("Opacity must be a whole number from 10 to 100.", error.Message);
    }

    [Theory]
    [InlineData("14.0")]
    [InlineData("14.9")]
    [InlineData("14,0")]
    [InlineData("1e1")]
    [InlineData("1E1")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("\u221e")]
    [InlineData("+14")]
    [InlineData("-14")]
    [InlineData("0x0E")]
    [InlineData("1_4")]
    [InlineData("14px")]
    [InlineData("14%")]
    [InlineData("2147483648")]
    [InlineData("99999999999999999999999999999")]
    [InlineData("\u0661\u0664")]
    [InlineData("\uff11\uff14")]
    public void NumericArgumentsRequireInvariantUnsignedDecimalIntegers(string value)
    {
        var fontError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse("font " + value));
        var opacityError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse("opacity " + value));
        Assert.StartsWith("Font size must be a whole number from 8 to 24.", fontError.Message);
        Assert.StartsWith("Opacity must be a whole number from 10 to 100.", opacityError.Message);
        Assert.Equal("text", fontError.ParamName);
        Assert.Equal("text", opacityError.ParamName);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void ParsingAndSuggestionsDoNotDependOnCurrentCulture(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            Assert.Equal(new(KeyboardCommandKind.Clarify, "on"), KeyboardCommands.Parse("CLARIFY ON"));
            Assert.Equal(new(KeyboardCommandKind.Source, "mic"), KeyboardCommands.Parse("SOURCE MIC"));
            Assert.Equal(new(KeyboardCommandKind.Font, Value: 14), KeyboardCommands.Parse("FONT 14"));
            Assert.Equal(new(KeyboardCommandKind.Opacity, Value: 55), KeyboardCommands.Parse("OPACITY 55"));
            Assert.Equal<string>(["clarify on", "clarify off"], KeyboardCommands.Suggest(":CLARI"));
            Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse("font 14,0"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void MaximumInputLengthIncludesWhitespaceBeforeTrimming()
    {
        var atBoundary = "help".PadLeft(KeyboardCommands.MaximumCommandCharacters);
        Assert.Equal(128, atBoundary.Length);
        Assert.Equal(new(KeyboardCommandKind.Help), KeyboardCommands.Parse(atBoundary));
        Assert.Equal<string>(["help"], KeyboardCommands.Suggest(atBoundary));

        var overBoundary = " " + atBoundary;
        var parseError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(overBoundary));
        var suggestError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Suggest(overBoundary));
        Assert.StartsWith("Commands must be 128 characters or fewer.", parseError.Message);
        Assert.StartsWith("Commands must be 128 characters or fewer.", suggestError.Message);
    }

    [Fact]
    public void MaximumInputLengthIncludesTheLeadingColon()
    {
        var atBoundary = ":help".PadRight(128);
        Assert.Equal(new(KeyboardCommandKind.Help), KeyboardCommands.Parse(atBoundary));
        Assert.Equal<string>(["help"], KeyboardCommands.Suggest(atBoundary));
        Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(atBoundary + " "));
        Assert.Throws<ArgumentException>(() => KeyboardCommands.Suggest(atBoundary + " "));
    }

    [Fact]
    public void LengthLimitCountsUtf16CodeUnitsAndValidatesEvenWhenMaximumIsZero()
    {
        var text = string.Concat(Enumerable.Repeat("\U0001f600", 65));
        Assert.Equal(130, text.Length);
        var parseError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(text));
        var suggestError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Suggest(text, 0));
        Assert.StartsWith("Commands must be 128 characters or fewer.", parseError.Message);
        Assert.StartsWith("Commands must be 128 characters or fewer.", suggestError.Message);
    }

    [Theory]
    [InlineData('\0')]
    [InlineData('\u0001')]
    [InlineData('\b')]
    [InlineData('\v')]
    [InlineData('\f')]
    [InlineData('\u001b')]
    [InlineData('\u007f')]
    [InlineData('\u0085')]
    [InlineData('\u009f')]
    public void RejectsControlCharactersBeforeTrimmingOrSplitting(char control)
    {
        foreach (var text in new[] { $"{control}help", $"help{control}", $"source{control}mic" })
        {
            var parseError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(text));
            var suggestError = Assert.Throws<ArgumentException>(() => KeyboardCommands.Suggest(text));
            Assert.StartsWith("Commands cannot contain control characters.", parseError.Message);
            Assert.StartsWith("Commands cannot contain control characters.", suggestError.Message);
        }
    }

    [Theory]
    [InlineData("start; stop")]
    [InlineData("start && stop")]
    [InlineData("start | stop")]
    [InlineData("start\nstop")]
    [InlineData("start\r\nask")]
    [InlineData("font $(14)")]
    [InlineData("font `14`")]
    [InlineData("font %SIZE%")]
    [InlineData("font ${SIZE}")]
    [InlineData("font \"14\"")]
    [InlineData("source 'mic'")]
    [InlineData("source \"mic\"")]
    [InlineData("source mic # comment")]
    [InlineData("source mic > output")]
    [InlineData("source mic & ask")]
    [InlineData("!help")]
    [InlineData("/help")]
    [InlineData("@help")]
    [InlineData("help ..\\script.ps1")]
    [InlineData("powershell -Command help")]
    [InlineData("cmd /c help")]
    [InlineData("ignore previous instructions and ask")]
    public void InjectionShapedTextIsRejectedWithoutEchoingIt(string text)
    {
        var error = Assert.Throws<ArgumentException>(() => KeyboardCommands.Parse(text));
        Assert.DoesNotContain(text, error.Message);
        Assert.Equal("text", error.ParamName);
        Assert.Empty(KeyboardCommands.Suggest(text));
    }

    [Fact]
    public void ExamplesAreUniqueCanonicalCommandsCoveringEveryFamily()
    {
        var examples = KeyboardCommands.Examples;
        Assert.NotEmpty(examples);
        Assert.Equal(examples.Count, examples.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(examples, example =>
        {
            Assert.Equal(example.Trim().ToLowerInvariant(), example);
            Assert.DoesNotContain(":", example);
        });
        var kinds = examples.Select(example => KeyboardCommands.Parse(example).Kind).ToHashSet();
        Assert.True(kinds.SetEquals(Enum.GetValues<KeyboardCommandKind>()));
        Assert.Contains("font 14", examples);
        Assert.Contains("opacity 55", examples);
    }

    [Fact]
    public void ExamplesCannotBeMutatedThroughCollectionInterfaces()
    {
        var examples = KeyboardCommands.Examples;
        var snapshot = examples.ToArray();
        AssertReadOnly(examples);
        Assert.Equal<string>(snapshot, KeyboardCommands.Examples);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("unknown prefix")]
    [InlineData("")]
    public void SuggestionsCannotBeMutatedThroughCollectionInterfaces(string text)
    {
        var suggestions = KeyboardCommands.Suggest(text);
        var snapshot = suggestions.ToArray();
        AssertReadOnly(suggestions);
        Assert.Equal<string>(snapshot, KeyboardCommands.Suggest(text));
    }

    [Fact]
    public void SuggestionsUseStableCanonicalPrefixOrdering()
    {
        string[] expected = ["source mic", "source computer", "source both"];
        Assert.Equal<string>(expected, KeyboardCommands.Suggest("SoUrCe"));
        Assert.Equal<string>(expected, KeyboardCommands.Suggest(" \t: SOURCE \r\n", 20));
        Assert.Equal<string>(expected, KeyboardCommands.Suggest("source"));
        Assert.Equal<string>(["source mic"], KeyboardCommands.Suggest(" : sOuRcE \t M"));
        Assert.Equal<string>(["record start", "record stop"], KeyboardCommands.Suggest("record"));
        Assert.Equal<string>(["font 14"], KeyboardCommands.Suggest("font"));
        Assert.Equal<string>(["copy question", "copy answer"], KeyboardCommands.Suggest("copy"));
    }

    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(19, 19)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(int.MaxValue, 20)]
    public void SuggestionMaximumIsClampedWithoutReordering(int maximum, int expectedCount)
    {
        Assert.Equal<string>(KeyboardCommands.Examples.Take(expectedCount), KeyboardCommands.Suggest("", maximum));
        Assert.Equal<string>(KeyboardCommands.Examples.Take(5), KeyboardCommands.Suggest(""));
    }

    [Fact]
    public void SuggestionMaximumDoesNotPadPartialMatches() =>
        Assert.Equal<string>(["theme dark", "theme light", "theme plain"], KeyboardCommands.Suggest("theme", 20));

    [Theory]
    [InlineData("unknown prefix")]
    [InlineData("font 99")]
    [InlineData("font 8")]
    [InlineData("::start")]
    [InlineData("source microphone")]
    [InlineData("run")]
    public void MissingSuggestionIsOrdinaryNoMatchNotAParseFailure(string text) =>
        Assert.Empty(KeyboardCommands.Suggest(text));

    [Fact]
    public void SuggestionCallsLeavePreviouslyReturnedDataUnchanged()
    {
        var first = KeyboardCommands.Suggest("copy");
        _ = KeyboardCommands.Suggest("theme", 1);
        _ = KeyboardCommands.Parse("help");
        Assert.Equal<string>(["copy question", "copy answer"], first);
    }

    [Fact]
    public void PublicBoundsMatchSliderAndInputContract()
    {
        Assert.Equal(128, KeyboardCommands.MaximumCommandCharacters);
        Assert.Equal(8, KeyboardCommands.MinimumFontSize);
        Assert.Equal(24, KeyboardCommands.MaximumFontSize);
    }

    private static void AssertReadOnly(IReadOnlyList<string> values)
    {
        Assert.IsNotType<string[]>(values);
        var list = Assert.IsAssignableFrom<IList<string>>(values);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.Add("injected"));
        Assert.Throws<NotSupportedException>(() => list.Clear());
        if (list.Count > 0)
        {
            Assert.Throws<NotSupportedException>(() => list[0] = "injected");
            Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        }
        var untyped = Assert.IsAssignableFrom<System.Collections.IList>(values);
        Assert.True(untyped.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => untyped.Add("injected"));
        Assert.Throws<NotSupportedException>(() => untyped.Clear());
    }
}
