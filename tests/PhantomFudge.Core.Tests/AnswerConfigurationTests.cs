using System.Text.Json;
using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class AnswerConfigurationTests
{
    [Theory]
    [InlineData("null", typeof(InvalidDataException))]
    [InlineData("{\"mode\":\"Unknown\"}", typeof(JsonException))]
    [InlineData("{\"mode\":\"SnapshotOnly\",\"unexpected\":true}", typeof(JsonException))]
    [InlineData("{\"mode\":3}", typeof(JsonException))]
    public void InvalidSettingsAreExplicitErrors(string content, Type expected)
    {
        var path = Path.Combine(Path.GetTempPath(), "phantom-answer-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, content);
            Assert.Throws(expected, () => AnswerConfiguration.LoadFromFile(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OversizedSettingsDoNotLoadAndMissingSettingsNeverSend()
    {
        var path = Path.Combine(Path.GetTempPath(), "phantom-answer-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            Assert.Equal(AnswerProviderMode.SnapshotOnly, AnswerConfiguration.LoadFromFile(path).Mode);
            File.WriteAllText(path, new string(' ', 8193));
            Assert.Throws<InvalidDataException>(() => AnswerConfiguration.LoadFromFile(path));
        }
        finally { File.Delete(path); }
    }
}
