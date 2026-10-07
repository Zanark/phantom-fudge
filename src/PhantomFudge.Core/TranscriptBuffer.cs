using System.Globalization;
using System.Text;

namespace PhantomFudge.Core;

public sealed class TranscriptBuffer
{
    public const int MaximumCharacters = 250_000;
    private readonly StringBuilder text = new();
    private int lastSegmentId;

    public string Text => text.ToString();
    public string Partial { get; private set; } = "";
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
        Partial = "";
    }

    public void SetPartial(string value)
    {
        if (value.Length > 16_384)
            throw new InvalidDataException("The partial transcript is too large.");
        Partial = value;
    }

    public bool AddFinal(int segmentId, string value, double? startSeconds)
    {
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
        var line = value.Length == 0 ? "" : prefix + value + Environment.NewLine;
        if (text.Length + line.Length > MaximumCharacters)
        {
            IsFull = true;
            throw new InvalidOperationException("Transcript memory limit reached. Export and clear before restarting.");
        }

        lastSegmentId = segmentId;
        Partial = "";
        text.Append(line);
        return line.Length > 0;
    }

    public string ExportText() => Partial.Length == 0
        ? Text
        : Text + Environment.NewLine + "[Unfinished recognition] " + Partial + Environment.NewLine;

    public void Clear()
    {
        text.Clear();
        Partial = "";
        lastSegmentId = 0;
        IsFull = false;
    }
}
