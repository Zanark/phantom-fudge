using System.Globalization;
using System.Text;

namespace PhantomFudge.Core;

public sealed class TranscriptBuffer
{
    public const int MaximumCharacters = 250_000;
    private readonly StringBuilder text = new();
    private readonly Dictionary<int, string> partials = [];
    private int lastSegmentId;

    public string Text => text.ToString();
    public string Partial => string.Join(Environment.NewLine, partials
        .Where(pair => pair.Value.Length > 0).OrderBy(pair => pair.Key)
        .Select(pair => SourcePrefix(pair.Key) + pair.Value));
    public bool IsFull { get; private set; }
    public bool HasContent => text.Length > 0 || Partial.Length > 0;

    public void BeginSession()
    {
        if (IsFull)
            throw new InvalidOperationException("Export and clear the full transcript before starting.");
        var unfinished = Partial.Length == 0 ? "" :
            Environment.NewLine + "[Unfinished recognition] " + Partial + Environment.NewLine;
        var separator = text.Length > 0 || unfinished.Length > 0 ? "\n--- New session ---\n" : "";
        if (text.Length + unfinished.Length + separator.Length > MaximumCharacters)
        {
            IsFull = true;
            throw new InvalidOperationException("Export and clear the full transcript before starting.");
        }
        text.Append(unfinished);
        text.Append(separator);
        lastSegmentId = 0;
        partials.Clear();
    }

    public void SetPartial(string value, AudioSource? source = null)
    {
        if (value.Length > 16_384)
            throw new InvalidDataException("The partial transcript is too large.");
        partials[SourceKey(source)] = value;
    }

    public bool AddFinal(int segmentId, string value, double? startSeconds, AudioSource? source = null)
    {
        var sourceKey = SourceKey(source);
        if (segmentId < 1 || value.Length > 16_384 ||
            (startSeconds.HasValue && (!double.IsFinite(startSeconds.Value) ||
                startSeconds < 0 || startSeconds > 2_592_000)))
            throw new InvalidDataException("Invalid transcript segment.");
        if (segmentId <= lastSegmentId)
            return false;

        var prefix = startSeconds.HasValue
            ? "[" + ((int)(startSeconds.Value / 3600)).ToString("00", CultureInfo.InvariantCulture)
                + TimeSpan.FromSeconds(startSeconds.Value).ToString(@"\:mm\:ss", CultureInfo.InvariantCulture) + "] "
            : "";
        var line = value.Length == 0 ? "" : prefix + SourcePrefix(sourceKey) + value + Environment.NewLine;
        if (text.Length + line.Length > MaximumCharacters)
        {
            IsFull = true;
            throw new InvalidOperationException("Transcript memory limit reached. Export and clear before restarting.");
        }

        lastSegmentId = segmentId;
        partials.Remove(sourceKey);
        text.Append(line);
        return line.Length > 0;
    }

    public string ExportText() => Partial.Length == 0
        ? Text
        : Text + Environment.NewLine + "[Unfinished recognition] " + Partial + Environment.NewLine;

    public void Clear()
    {
        text.Clear();
        partials.Clear();
        lastSegmentId = 0;
        IsFull = false;
    }

    private static int SourceKey(AudioSource? source) => source switch
    {
        null => 0,
        AudioSource.Computer => 1,
        AudioSource.Microphone => 2,
        _ => throw new InvalidDataException("Invalid transcript audio source.")
    };

    private static string SourcePrefix(int source) => source == 0
        ? "" : "[" + ((AudioSource)source).DisplayName() + "] ";
}
