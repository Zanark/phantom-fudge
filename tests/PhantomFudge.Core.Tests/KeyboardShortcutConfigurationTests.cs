using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PhantomFudge.Core.Tests;

public sealed class KeyboardShortcutConfigurationTests : IDisposable
{
    private readonly string directory = ".keyboard-shortcut-tests-" + Guid.NewGuid().ToString("N");
    private readonly string filePath;

    public KeyboardShortcutConfigurationTests()
    {
        Directory.CreateDirectory(directory);
        filePath = Path.Combine(directory, "keyboard.json");
    }

    public static TheoryData<string, uint> ValidKeys
    {
        get
        {
            var data = new TheoryData<string, uint>();
            for (var key = 'A'; key <= 'Z'; key++)
                if (key != 'E')
                    data.Add(key.ToString(), key);
            for (var number = 1; number <= 11; number++)
                data.Add("F" + number.ToString(CultureInfo.InvariantCulture), (uint)(0x70 + number - 1));
            return data;
        }
    }

    [Fact]
    public void DefaultSettingsEnableKAndUseTheLocalApplicationDataPath()
    {
        var settings = new KeyboardShortcutSettings();
        settings.Validate();
        Assert.True(settings.Enabled);
        Assert.Equal("K", settings.ActivationKey);
        Assert.Equal(0x4Bu, settings.VirtualKey);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhantomFudge", "keyboard.json"), KeyboardShortcutConfiguration.FilePath);
    }

    [Theory]
    [MemberData(nameof(ValidKeys))]
    public void AllowedKeysHaveExactCaseInsensitiveVirtualKeys(string key, uint virtualKey)
    {
        foreach (var text in new[] { key, key.ToLowerInvariant() })
        {
            foreach (var enabled in new[] { true, false })
            {
                var settings = new KeyboardShortcutSettings(enabled, text);
                settings.Validate();
                Assert.Equal(virtualKey, settings.VirtualKey);
                Assert.Equal(text, settings.ActivationKey);
                WriteSettings(enabled, text);
                var loaded = KeyboardShortcutConfiguration.Load(filePath);
                Assert.Equal(settings, loaded);
                Assert.Equal(virtualKey, loaded.VirtualKey);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("E")]
    [InlineData("e")]
    [InlineData(" K")]
    [InlineData("K ")]
    [InlineData("KK")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("F0")]
    [InlineData("F12")]
    [InlineData("f12")]
    [InlineData("F13")]
    [InlineData("F99")]
    [InlineData("F01")]
    [InlineData("F001")]
    [InlineData("F+1")]
    [InlineData("F-1")]
    [InlineData("F 1")]
    [InlineData("F1.0")]
    [InlineData("F1e1")]
    [InlineData("F\u0661")]
    [InlineData("Escape")]
    [InlineData("Space")]
    [InlineData("Ctrl+K")]
    [InlineData("\u017f")]
    [InlineData("\u0131")]
    [InlineData("\uff2b")]
    [InlineData("\0")]
    [InlineData("K\n")]
    [InlineData("K;ask")]
    [InlineData("$(K)")]
    public void UnsupportedKeysRejectEvenWhenTheShortcutIsDisabled(string key)
    {
        foreach (var enabled in new[] { true, false })
        {
            var settings = new KeyboardShortcutSettings(enabled, key);
            var error = Assert.Throws<ArgumentException>(() => settings.Validate());
            Assert.StartsWith("Activation key must be A-Z except E, or F1-F11.", error.Message);
            Assert.Equal("ActivationKey", error.ParamName);
            Assert.Throws<ArgumentException>(() => settings.VirtualKey);
            WriteSettings(enabled, key);
            Assert.Throws<ArgumentException>(() => KeyboardShortcutConfiguration.Load(filePath));
        }
    }

    [Fact]
    public void NullActivationKeyDoesNotBypassValidation()
    {
        var settings = new KeyboardShortcutSettings(ActivationKey: null!);
        Assert.Throws<ArgumentException>(() => settings.Validate());
        Assert.Throws<ArgumentException>(() => settings.VirtualKey);
    }

    [Fact]
    public void MissingFileReturnsDefaultsWithoutCreatingAFile()
    {
        Assert.Equal(new KeyboardShortcutSettings(), KeyboardShortcutConfiguration.Load(filePath));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void MissingDirectoryReturnsDefaultsWithoutCreatingDirectories()
    {
        var missingDirectory = Path.Combine(directory, "missing");
        var path = Path.Combine(missingDirectory, "keyboard.json");
        Assert.Equal(new KeyboardShortcutSettings(), KeyboardShortcutConfiguration.Load(path));
        Assert.False(Directory.Exists(missingDirectory));
    }

    [Fact]
    public void OmittedAndNullPathsUseTheConfiguredFilePath()
    {
        var originalPath = KeyboardShortcutConfiguration.FilePath;
        try
        {
            KeyboardShortcutConfiguration.FilePath = filePath;
            Assert.Equal(new KeyboardShortcutSettings(), KeyboardShortcutConfiguration.Load());
            WriteSettings(false, "f11");
            var expected = new KeyboardShortcutSettings(false, "f11");
            Assert.Equal(expected, KeyboardShortcutConfiguration.Load());
            Assert.Equal(expected, KeyboardShortcutConfiguration.Load(null));
        }
        finally
        {
            KeyboardShortcutConfiguration.FilePath = originalPath;
        }
    }

    [Fact]
    public void ExplicitPathDoesNotReadTheDefaultFile()
    {
        var originalPath = KeyboardShortcutConfiguration.FilePath;
        try
        {
            var invalidDefault = Path.Combine(directory, "invalid-default.json");
            File.WriteAllText(invalidDefault, "not json");
            KeyboardShortcutConfiguration.FilePath = invalidDefault;
            WriteSettings(true, "F1");
            Assert.Equal(new KeyboardShortcutSettings(true, "F1"), KeyboardShortcutConfiguration.Load(filePath));
        }
        finally
        {
            KeyboardShortcutConfiguration.FilePath = originalPath;
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("\"K\"")]
    [InlineData("{}")]
    [InlineData("""{"schemaVersion":1}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true}""")]
    [InlineData("""{"schemaVersion":1,"activationKey":"K"}""")]
    [InlineData("""{"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":0,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":2,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":-1,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1.0,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1e0,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":2147483648,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":"1","enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":true,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":null,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":{},"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":1,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":"true","activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":null,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":[],"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":75}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":null}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":true}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":{}}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":["K"]}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":"K","unknown":true}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":"K","modifiers":"Alt"}""")]
    [InlineData("""{"schemaVersion":1,"Enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"SchemaVersion":1,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"ActivationKey":"K"}""")]
    public void InvalidSchemaNeverSilentlyLoadsDefaults(string json)
    {
        File.WriteAllText(filePath, json);
        Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
        Assert.Equal(json, File.ReadAllText(filePath));
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"schemaVersion":1,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"enabled":false,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":"K","activationKey":"F1"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"\u0065nabled":false,"activationKey":"K"}""")]
    public void DuplicatePropertiesAreRejectedIncludingEscapedNames(string json)
    {
        File.WriteAllText(filePath, json);
        var error = Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
        Assert.StartsWith("Keyboard shortcut settings cannot repeat properties.", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":"K",}""")]
    [InlineData("""{/*comment*/"schemaVersion":1,"enabled":true,"activationKey":"K"}""")]
    [InlineData("""{"schemaVersion":1,"enabled":true,"activationKey":"K"}{}""")]
    [InlineData("""{'schemaVersion':1,'enabled':true,'activationKey':'K'}""")]
    public void MalformedJsonIsAnExplicitParseError(string json)
    {
        File.WriteAllText(filePath, json);
        Assert.ThrowsAny<JsonException>(() => KeyboardShortcutConfiguration.Load(filePath));
    }

    [Fact]
    public void PropertyOrderingWhitespaceAndEscapedNamesDoNotChangeTheSettings()
    {
        File.WriteAllText(filePath, """
            {
                "activationKey": "\u006b",
                "enabled": false,
                "schema\u0056ersion": 1
            }
            """);
        Assert.Equal(new KeyboardShortcutSettings(false, "k"), KeyboardShortcutConfiguration.Load(filePath));
    }

    [Fact]
    public void Utf8BomIsAcceptedAndFileContentsRemainUntouched()
    {
        const string json = """{"schemaVersion":1,"enabled":true,"activationKey":"K"}""";
        byte[] content = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(json)];
        File.WriteAllBytes(filePath, content);
        Assert.Equal(new KeyboardShortcutSettings(), KeyboardShortcutConfiguration.Load(filePath));
        Assert.Equal(content, File.ReadAllBytes(filePath));
    }

    [Fact]
    public void InvalidUtf8IsRejectedWithoutReplacementCharacters()
    {
        byte[] content = [.. Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"enabled\":true,\"activationKey\":\""), 0xff,
            .. Encoding.UTF8.GetBytes("\"}")];
        File.WriteAllBytes(filePath, content);
        var error = Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
        Assert.StartsWith("Keyboard shortcut settings must use valid UTF-8.", error.Message);
    }

    [Fact]
    public void ByteLimitIsInclusiveAndCheckedBeforeJsonParsing()
    {
        const string json = """{"schemaVersion":1,"enabled":true,"activationKey":"K"}""";
        var exactLimit = json.PadRight(1024);
        File.WriteAllText(filePath, exactLimit, new UTF8Encoding(false));
        Assert.Equal(1024, new FileInfo(filePath).Length);
        Assert.Equal(new KeyboardShortcutSettings(), KeyboardShortcutConfiguration.Load(filePath));
        File.AppendAllText(filePath, " ");
        var error = Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
        Assert.StartsWith("Keyboard shortcut settings exceed the 1024-byte limit.", error.Message);
    }

    [Fact]
    public void Utf8BomCountsTowardTheByteLimit()
    {
        const string json = """{"schemaVersion":1,"enabled":true,"activationKey":"K"}""";
        byte[] bytes = [.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(json.PadRight(1021))];
        File.WriteAllBytes(filePath, bytes);
        Assert.Equal(new KeyboardShortcutSettings(), KeyboardShortcutConfiguration.Load(filePath));
        File.AppendAllText(filePath, " ");
        Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
    }

    [Fact]
    public void SizeValidationCountsBytesRatherThanCharacters()
    {
        var json = JsonSerializer.Serialize(new { schemaVersion = 1, enabled = true, activationKey = "K" });
        json = json.Replace("\"K\"", "\"" + new string('\u00e9', 510) + "\"", StringComparison.Ordinal);
        Assert.True(json.Length < 1024);
        Assert.True(Encoding.UTF8.GetByteCount(json) > 1024);
        File.WriteAllText(filePath, json, new UTF8Encoding(false));
        var error = Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
        Assert.StartsWith("Keyboard shortcut settings exceed the 1024-byte limit.", error.Message);
    }

    [Fact]
    public void LargeFilesAreRejected()
    {
        File.WriteAllBytes(filePath, new byte[64 * 1024]);
        Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
    }

    [Fact]
    public void EmptyPathIsNotTreatedAsAMissingFile() =>
        Assert.Throws<ArgumentException>(() => KeyboardShortcutConfiguration.Load(""));

    [Fact]
    public void ExistingDirectoryIsNotTreatedAsAMissingFile() =>
        Assert.Throws<UnauthorizedAccessException>(() => KeyboardShortcutConfiguration.Load(directory));

    [Fact]
    public void LockedFileFailureIsNotReplacedWithDefaults()
    {
        WriteSettings(true, "K");
        using var locked = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => KeyboardShortcutConfiguration.Load(filePath));
    }

    [Fact]
    public void LoadsReflectCurrentContentsAndDoNotReuseThePreviousValidValue()
    {
        WriteSettings(true, "F1");
        var first = KeyboardShortcutConfiguration.Load(filePath);
        WriteSettings(false, "F11");
        Assert.Equal(new KeyboardShortcutSettings(false, "F11"), KeyboardShortcutConfiguration.Load(filePath));
        Assert.Equal(new KeyboardShortcutSettings(true, "F1"), first);
        File.WriteAllText(filePath, "{}");
        Assert.Throws<InvalidDataException>(() => KeyboardShortcutConfiguration.Load(filePath));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void KeyResolutionAndSchemaParsingAreCultureIndependent(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            WriteSettings(true, "i");
            Assert.Equal(0x49u, KeyboardShortcutConfiguration.Load(filePath).VirtualKey);
            WriteSettings(true, "f11");
            Assert.Equal(0x7Au, KeyboardShortcutConfiguration.Load(filePath).VirtualKey);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private void WriteSettings(bool enabled, string activationKey) =>
        File.WriteAllText(filePath, JsonSerializer.Serialize(new { schemaVersion = 1, enabled, activationKey }));

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
