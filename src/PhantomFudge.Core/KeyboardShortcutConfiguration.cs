using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PhantomFudge.Core;

public sealed record KeyboardShortcutSettings(bool Enabled = true, string ActivationKey = "K")
{
    public uint VirtualKey => ResolveVirtualKey();

    public void Validate() => _ = ResolveVirtualKey();

    private uint ResolveVirtualKey()
    {
        if (ActivationKey is { Length: 1 } letter && char.IsAsciiLetter(letter[0]))
        {
            var key = char.ToUpperInvariant(letter[0]);
            if (key != 'E')
                return key;
        }
        if (ActivationKey is { Length: 2 or 3 } functionKey && functionKey[0] is 'F' or 'f' &&
            functionKey[1] != '0' &&
            int.TryParse(functionKey.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number is >= 1 and <= 11)
            return (uint)(0x70 + number - 1);

        throw new ArgumentException("Activation key must be A-Z except E, or F1-F11.", nameof(ActivationKey));
    }
}

public static class KeyboardShortcutConfiguration
{
    private const int MaximumBytes = 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomFudge", "keyboard.json");

    public static KeyboardShortcutSettings Load(string? filePath = null)
    {
        FileStream source;
        try
        {
            source = new FileStream(filePath ?? FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (FileNotFoundException)
        {
            return new();
        }
        catch (DirectoryNotFoundException)
        {
            return new();
        }

        using (source)
        {
            var bytes = new byte[MaximumBytes + 1];
            var count = source.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            if (count > MaximumBytes)
                throw new InvalidDataException("Keyboard shortcut settings exceed the 1024-byte limit.");

            var offset = bytes.AsSpan(0, count).StartsWith(Encoding.UTF8.Preamble) ? 3 : 0;
            string json;
            try
            {
                json = StrictUtf8.GetString(bytes, offset, count - offset);
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidDataException("Keyboard shortcut settings must use valid UTF-8.");
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Keyboard shortcut settings must be a JSON object.");

            var properties = new HashSet<string>(StringComparer.Ordinal);
            var enabled = true;
            var activationKey = "";
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!properties.Add(property.Name))
                    throw new InvalidDataException("Keyboard shortcut settings cannot repeat properties.");

                switch (property.Name)
                {
                    case "schemaVersion":
                        if (property.Value.ValueKind != JsonValueKind.Number ||
                            !property.Value.TryGetInt32(out var version) || version != 1)
                            throw new InvalidDataException("Keyboard shortcut settings require schemaVersion 1.");
                        break;
                    case "enabled":
                        if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                            throw new InvalidDataException("Keyboard shortcut enabled must be a boolean.");
                        enabled = property.Value.GetBoolean();
                        break;
                    case "activationKey":
                        if (property.Value.ValueKind != JsonValueKind.String)
                            throw new InvalidDataException("Keyboard shortcut activationKey must be a string.");
                        activationKey = property.Value.GetString()!;
                        break;
                    default:
                        throw new InvalidDataException("Keyboard shortcut settings contain an unknown property.");
                }
            }

            if (properties.Count != 3)
                throw new InvalidDataException(
                    "Keyboard shortcut settings require schemaVersion, enabled, and activationKey.");

            var settings = new KeyboardShortcutSettings(enabled, activationKey);
            settings.Validate();
            return settings;
        }
    }
}
