using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhantomFudge.Core.Answers;

public static class AnswerConfiguration
{
    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhantomFudge");
    public static string FilePath => Path.Combine(DirectoryPath, "answers.json");
    public static string CopilotProfilePath => Path.Combine(DirectoryPath, "copilot-profile");

    public static AnswerSettings Load() => LoadFromFile(FilePath);

    internal static AnswerSettings LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new(AnswerProviderMode.SnapshotOnly);
        using var file = File.OpenRead(path);
        if (file.Length > 8192)
            throw new InvalidDataException("Answer settings are too large.");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Converters = { new JsonStringEnumConverter<AnswerProviderMode>(allowIntegerValues: false) }
        };
        var settings = JsonSerializer.Deserialize<AnswerSettings>(file, options)
            ?? throw new InvalidDataException("Answer settings are empty.");
        settings.Validate();
        return settings;
    }
}
