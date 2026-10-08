using System.Diagnostics;

namespace PhantomFudge.Core;

/// <summary>Explicit local selection/edit state. Mutations and Freeze belong to the same caller (UI dispatcher).</summary>
public sealed class QuestionDraft
{
    public const int MaximumCharacters = 8_192;
    public const int MaximumSelectedSegments = 256;

    public Guid QuestionId { get; private set; } = Guid.NewGuid();
    public long Revision { get; private set; }
    public string Text { get; private set; } = "";
    public bool IsProvisional => SelectedSegments.Any(segment => !segment.IsFinal);
    public bool UserEdited { get; private set; }
    public IReadOnlyList<TranscriptSegment> SelectedSegments { get; private set; } = Array.AsReadOnly<TranscriptSegment>([]);

    public void ReplaceSelection(IEnumerable<TranscriptSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var selected = new List<TranscriptSegment>();
        var keys = new HashSet<TranscriptSegmentKey>();
        Guid? sessionId = null;
        foreach (var segment in segments)
        {
            if (segment is null)
                throw new ArgumentException("A selection cannot contain null segments.", nameof(segments));
            if (sessionId.HasValue && segment.Key.CaptureSessionId != sessionId)
                throw new ArgumentException("Select segments from only one capture session.", nameof(segments));
            sessionId = segment.Key.CaptureSessionId;
            if (!keys.Add(segment.Key))
                continue;
            if (selected.Count == MaximumSelectedSegments)
                throw new ArgumentException($"Select no more than {MaximumSelectedSegments} segments.", nameof(segments));
            selected.Add(segment.Copy());
        }

        var text = Compose(selected, refreshing: false);
        if (!UserEdited && text == Text && selected.SequenceEqual(SelectedSegments))
            return;
        var revision = checked(Revision + 1);
        SelectedSegments = Array.AsReadOnly(selected.ToArray());
        Text = text;
        UserEdited = false;
        Revision = revision;
    }

    public void EditText(string text)
    {
        ValidateText(text);
        if (text != Text)
        {
            var revision = checked(Revision + 1);
            Text = text;
            Revision = revision;
        }
        // Even reasserting the same text is an explicit edit, not permission for later ASR to replace it.
        UserEdited = true;
    }

    public void Refresh(StructuredTranscriptStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (UserEdited || SelectedSegments.Count == 0)
            return;

        var current = store.Segments.ToDictionary(segment => segment.Key);
        var selected = SelectedSegments.Select(previous =>
            !previous.IsFinal && current.TryGetValue(previous.Key, out var latest) && latest.Revision > previous.Revision
                ? latest.Copy() : previous).ToArray();
        if (selected.SequenceEqual(SelectedSegments))
            return;

        var text = Compose(selected, refreshing: true);
        var revision = checked(Revision + 1);
        SelectedSegments = Array.AsReadOnly(selected);
        Text = text;
        Revision = revision;
    }

    public void Clear()
    {
        QuestionId = Guid.NewGuid();
        Revision = 0;
        Text = "";
        UserEdited = false;
        SelectedSegments = Array.AsReadOnly<TranscriptSegment>([]);
    }

    public QuestionSnapshot Freeze()
    {
        ValidateText(Text);
        if (string.IsNullOrWhiteSpace(Text))
            throw new InvalidOperationException("Enter or select a non-blank question before freezing.");
        return new(QuestionId, Revision, Text, SelectedSegments, IsProvisional, UserEdited, Stopwatch.GetTimestamp());
    }

    private static void ValidateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCharacters)
            throw new ArgumentException($"Question text cannot exceed {MaximumCharacters} characters.", nameof(text));
    }

    private static string Compose(IReadOnlyList<TranscriptSegment> selected, bool refreshing)
    {
        long length = 0;
        var nonemptyCount = 0;
        foreach (var segment in selected)
        {
            if (segment.Text.Length == 0)
                continue;
            if (nonemptyCount++ > 0)
                length += Environment.NewLine.Length;
            length += segment.Text.Length;
            if (length > MaximumCharacters)
            {
                var message = $"The selected question exceeds {MaximumCharacters} characters. The previous draft is unchanged; edit or select less text.";
                if (refreshing)
                    throw new InvalidOperationException(message);
                throw new ArgumentException(message, "segments");
            }
        }
        return string.Join(Environment.NewLine, selected.Where(segment => segment.Text.Length > 0).Select(segment => segment.Text));
    }
}
