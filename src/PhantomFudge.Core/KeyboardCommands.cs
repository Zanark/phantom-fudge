using System.Globalization;

namespace PhantomFudge.Core;

public enum KeyboardCommandKind
{
    Start, Stop, Source, Devices, Clarify, Confirm, Font, CopyQuestion, CopyAnswer,
    ClearQuestion, Cancel, Theme, Opacity, Ask, Mask, Help
}

public sealed record KeyboardCommand(KeyboardCommandKind Kind, string? Argument = null, int? Value = null);

public static class KeyboardCommands
{
    public const int MaximumCommandCharacters = 128;
    public const int MinimumFontSize = 8, MaximumFontSize = 24;

    public static IReadOnlyList<string> Examples { get; } = Array.AsReadOnly(new[]
    {
        "start", "stop", "source mic", "source computer", "source both", "devices",
        "clarify on", "clarify off", "confirm", "font 14", "copy question", "copy answer",
        "clear question", "cancel", "theme dark", "theme light", "theme plain", "opacity 55",
        "ask", "mask", "help", "record start", "record stop"
    });

    public static KeyboardCommand Parse(string text)
    {
        var tokens = Tokenize(text);
        if (tokens.Length == 0)
            throw new ArgumentException("Enter a built-in command. Type help for examples.", nameof(text));

        return tokens switch
        {
            ["start"] or ["record", "start"] => new(KeyboardCommandKind.Start),
            ["stop"] or ["record", "stop"] => new(KeyboardCommandKind.Stop),
            ["source", "mic" or "computer" or "both"] => new(KeyboardCommandKind.Source, tokens[1]),
            ["devices"] => new(KeyboardCommandKind.Devices),
            ["clarify", "on" or "off"] => new(KeyboardCommandKind.Clarify, tokens[1]),
            ["confirm"] => new(KeyboardCommandKind.Confirm),
            ["font", var value] => new(KeyboardCommandKind.Font, Value: ParseInteger(value,
                MinimumFontSize, MaximumFontSize, "Font size must be a whole number from 8 to 24.")),
            ["copy", "question"] => new(KeyboardCommandKind.CopyQuestion),
            ["copy", "answer"] => new(KeyboardCommandKind.CopyAnswer),
            ["clear", "question"] => new(KeyboardCommandKind.ClearQuestion),
            ["cancel"] => new(KeyboardCommandKind.Cancel),
            ["theme", "dark" or "light" or "plain"] => new(KeyboardCommandKind.Theme, tokens[1]),
            ["opacity", var value] => new(KeyboardCommandKind.Opacity, Value: ParseInteger(value,
                10, 100, "Opacity must be a whole number from 10 to 100.")),
            ["ask"] => new(KeyboardCommandKind.Ask),
            ["mask"] => new(KeyboardCommandKind.Mask),
            ["help"] => new(KeyboardCommandKind.Help),
            _ => throw new ArgumentException(
                "Unknown command or invalid arguments. Type help for built-in commands.", nameof(text))
        };
    }

    public static IReadOnlyList<string> Suggest(string text, int maximum = 5)
    {
        var prefix = string.Join(" ", Tokenize(text));
        return Array.AsReadOnly(Examples
            .Where(example => example.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(maximum, 0, 20))
            .ToArray());
    }

    private static string[] Tokenize(string text)
    {
        if (text is null)
            throw new ArgumentNullException(nameof(text), "Enter a built-in command.");
        if (text.Length > MaximumCommandCharacters)
            throw new ArgumentException("Commands must be 128 characters or fewer.", nameof(text));
        if (text.Any(character => char.IsControl(character) && character is not ('\t' or '\r' or '\n')))
            throw new ArgumentException("Commands cannot contain control characters.", nameof(text));

        var command = text.Trim();
        if (command.StartsWith(':'))
            command = command[1..].Trim();
        return command.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int ParseInteger(string text, int minimum, int maximum, string message)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
            value < minimum || value > maximum)
            throw new ArgumentException(message, nameof(text));
        return value;
    }
}
