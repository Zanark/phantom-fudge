namespace PhantomFudge.Core.Tests;

public sealed class QuestionDraftTests
{
    private static readonly Guid Session = Guid.Parse("30000000-0000-0000-0000-000000000001");
    private static readonly Guid NextSession = Guid.Parse("30000000-0000-0000-0000-000000000002");

    [Fact]
    public void NewDraftIsEmptyAndDoesNotImplicitlySelectTranscript()
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Microphone, 1, "unselected question?", null, null);
        var draft = new QuestionDraft();
        draft.Refresh(store);
        Assert.NotEqual(Guid.Empty, draft.QuestionId);
        Assert.Equal(0, draft.Revision);
        Assert.Equal("", draft.Text);
        Assert.Empty(draft.SelectedSegments);
        Assert.False(draft.IsProvisional);
        Assert.False(draft.UserEdited);
        Assert.Throws<InvalidOperationException>(() => draft.Freeze());
    }

    [Fact]
    public void SelectionPreservesExplicitOrderAndDeduplicatesByKey()
    {
        var first = Final(1, "first");
        var second = Final(2, "second");
        var third = Final(3, "third");
        var draft = new QuestionDraft();
        draft.ReplaceSelection([third, first, third, second, first]);
        Assert.Equal(string.Join(Environment.NewLine, "third", "first", "second"), draft.Text);
        Assert.Equal([third.Key, first.Key, second.Key], draft.SelectedSegments.Select(segment => segment.Key));
        Assert.Equal(1, draft.Revision);
        Assert.False(draft.IsProvisional);
        Assert.False(draft.UserEdited);
        Assert.NotSame(third, draft.SelectedSegments[0]);
        Assert.NotSame(third.Key, draft.SelectedSegments[0].Key);
    }

    [Fact]
    public void DuplicateKeyUsesFirstExplicitOccurrenceNotAHiddenLaterRevision()
    {
        var key = new TranscriptSegmentKey(Session, AudioSource.Microphone, 0, 1);
        var first = new TranscriptSegment(key, 1, "chosen", false, null, null, null, true);
        var later = new TranscriptSegment(key, 2, "not chosen", false, null, null, null, true);
        var draft = new QuestionDraft();
        draft.ReplaceSelection([first, later]);
        Assert.Equal("chosen", draft.Text);
        Assert.Equal(1, Assert.Single(draft.SelectedSegments).Revision);
    }

    [Fact]
    public void CrossSourceCompositionRequiresExplicitSelection()
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Computer, 1, "remote context", null, null);
        store.AddFinal(Session, AudioSource.Microphone, 2, "unrelated earlier mic", null, null);
        store.SetPartial(Session, AudioSource.Microphone, "selected question");
        var draft = new QuestionDraft();
        draft.ReplaceSelection([store.Segments[2]]);
        Assert.Equal("selected question", draft.Text);
        Assert.Equal(AudioSource.Microphone, Assert.Single(draft.SelectedSegments).Key.Source);
        draft.ReplaceSelection([store.Segments[2], store.Segments[0]]);
        Assert.Equal("selected question" + Environment.NewLine + "remote context", draft.Text);
        Assert.DoesNotContain("unrelated", draft.Text);
        Assert.True(draft.IsProvisional);
    }

    [Fact]
    public void ComposedTextIsExactAndNeverParsesRenderedSourceLabels()
    {
        var draft = new QuestionDraft();
        var first = Final(1, " \t[Computer] literal text\r\n");
        var second = Final(2, "cafe\u0301 \u2603  ");
        draft.ReplaceSelection([first, second]);
        Assert.Equal(first.Text + Environment.NewLine + second.Text, draft.Text);
        Assert.All(draft.SelectedSegments, segment => Assert.Equal(AudioSource.Microphone, segment.Key.Source));
        Assert.Equal(draft.Text, draft.Freeze().ExactText);
    }

    [Fact]
    public void RepeatingSameSelectionDoesNotCreateAnotherRevision()
    {
        var segment = Final(1, "same");
        var draft = new QuestionDraft();
        draft.ReplaceSelection([segment]);
        var selection = draft.SelectedSegments;
        var id = draft.QuestionId;
        draft.ReplaceSelection([segment, segment]);
        Assert.Equal(1, draft.Revision);
        Assert.Same(selection, draft.SelectedSegments);
        Assert.Equal(id, draft.QuestionId);
    }

    [Fact]
    public void SelectionCopiesListAndRecordsAndExposesAReadOnlySnapshot()
    {
        var first = Final(1, "selected");
        var inputs = new[] { first };
        var draft = new QuestionDraft();
        draft.ReplaceSelection(inputs);
        var selected = draft.SelectedSegments;
        inputs[0] = Final(2, "replacement outside draft");
        Assert.Equal("selected", draft.Text);
        Assert.Equal(first, Assert.Single(selected));
        Assert.NotSame(first, selected[0]);
        Assert.NotSame(first.Key, selected[0].Key);
        var list = Assert.IsAssignableFrom<IList<TranscriptSegment>>(selected);
        Assert.Throws<NotSupportedException>(() => list.Add(first));
        Assert.Throws<NotSupportedException>(() => list[0] = first);
        draft.Clear();
        Assert.Equal(first, Assert.Single(selected));
    }

    [Fact]
    public void RefreshChangesOnlySelectedPartialAndFinalizesItsSameKey()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "question");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var key = draft.SelectedSegments[0].Key;
        var questionId = draft.QuestionId;
        store.SetPartial(Session, AudioSource.Computer, "not selected");
        store.SetPartial(Session, AudioSource.Microphone, "question revised");
        draft.Refresh(store);
        Assert.Equal("question revised", draft.Text);
        Assert.Equal(2, draft.Revision);
        Assert.Equal(2, Assert.Single(draft.SelectedSegments).Revision);
        Assert.Equal(key, draft.SelectedSegments[0].Key);
        draft.Refresh(store);
        Assert.Equal(2, draft.Revision);

        store.AddFinal(Session, AudioSource.Microphone, 9, "question revised", 0, 1);
        draft.Refresh(store);
        Assert.Equal(3, draft.Revision);
        Assert.Equal(3, Assert.Single(draft.SelectedSegments).Revision);
        Assert.Equal(key, draft.SelectedSegments[0].Key);
        Assert.Equal(9, draft.SelectedSegments[0].ProtocolSegmentId);
        Assert.False(draft.IsProvisional);
        Assert.Equal(questionId, draft.QuestionId);
        store.SetPartial(Session, AudioSource.Microphone, "new unselected utterance");
        draft.Refresh(store);
        Assert.Equal("question revised", draft.Text);
        Assert.Single(draft.SelectedSegments);
        Assert.Equal(3, draft.Revision);
    }

    [Fact]
    public void RefreshRetainsSelectionOrderNotStoreOrder()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Computer, "computer");
        store.SetPartial(Session, AudioSource.Microphone, "mic");
        var draft = new QuestionDraft();
        draft.ReplaceSelection([store.Segments[1], store.Segments[0]]);
        store.AddFinal(Session, AudioSource.Computer, 1, "computer final", 0, 1);
        store.AddFinal(Session, AudioSource.Microphone, 2, "mic final", 0, 1);
        draft.Refresh(store);
        Assert.Equal("mic final" + Environment.NewLine + "computer final", draft.Text);
        Assert.False(draft.IsProvisional);
        Assert.Equal(AudioSource.Microphone, draft.SelectedSegments[0].Key.Source);
    }

    [Fact]
    public void UserEditsPreventLateAsrFromChangingTextOrProvenance()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "asr guess");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var selected = draft.SelectedSegments;
        const string edited = " \tMy exact correction\r\n ";
        draft.EditText(edited);
        var revision = draft.Revision;
        store.AddFinal(Session, AudioSource.Microphone, 1, "different ASR final", 0, 1);
        draft.Refresh(store);
        Assert.Equal(edited, draft.Text);
        Assert.True(draft.UserEdited);
        Assert.True(draft.IsProvisional);
        Assert.Equal(revision, draft.Revision);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal("asr guess", Assert.Single(draft.SelectedSegments).Text);
        Assert.Equal(1, draft.SelectedSegments[0].Revision);
    }

    [Fact]
    public void ReassertingIdenticalTextIsAnEditWithoutRevisionInflation()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "exact");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        draft.EditText("exact");
        Assert.Equal(1, draft.Revision);
        Assert.True(draft.UserEdited);
        store.SetPartial(Session, AudioSource.Microphone, "different");
        draft.Refresh(store);
        Assert.Equal("exact", draft.Text);
        Assert.Equal(1, draft.Revision);
    }

    [Fact]
    public void ExplicitReplacementAfterEditReenablesSelectedAsrRefresh()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "asr");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        draft.EditText("corrected");
        draft.ReplaceSelection(store.Segments);
        Assert.False(draft.UserEdited);
        Assert.Equal("asr", draft.Text);
        store.SetPartial(Session, AudioSource.Microphone, "updated");
        draft.Refresh(store);
        Assert.Equal("updated", draft.Text);
        Assert.Equal(4, draft.Revision);
    }

    [Fact]
    public void TypedQuestionDoesNotAcquireAsrProvenance()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "not selected");
        var draft = new QuestionDraft();
        const string text = "  What is local?\nKeep these bytes. \t";
        draft.EditText(text);
        draft.Refresh(store);
        Assert.Equal(text, draft.Text);
        Assert.True(draft.UserEdited);
        Assert.False(draft.IsProvisional);
        Assert.Empty(draft.SelectedSegments);
        Assert.Equal(text, draft.Freeze().ExactText);
    }

    [Fact]
    public void EmptySelectionExplicitlyClearsTypedAndSelectedState()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, "selected")]);
        draft.EditText("typed");
        var id = draft.QuestionId;
        draft.ReplaceSelection([]);
        Assert.Empty(draft.SelectedSegments);
        Assert.Equal("", draft.Text);
        Assert.False(draft.UserEdited);
        Assert.False(draft.IsProvisional);
        Assert.Equal(3, draft.Revision);
        Assert.Equal(id, draft.QuestionId);
        draft.ReplaceSelection([]);
        Assert.Equal(3, draft.Revision);
    }

    [Fact]
    public void EmptyFinalRemovesGuessedQuestionTextWithoutAddingNewUtterances()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "discarded recognition");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var key = draft.SelectedSegments[0].Key;
        store.AddFinal(Session, AudioSource.Microphone, 1, "", 0, 1);
        draft.Refresh(store);
        Assert.Equal("", draft.Text);
        Assert.False(draft.IsProvisional);
        Assert.Equal(key, Assert.Single(draft.SelectedSegments).Key);
        Assert.Equal("", draft.SelectedSegments[0].Text);
        Assert.Throws<InvalidOperationException>(() => draft.Freeze());
        store.SetPartial(Session, AudioSource.Microphone, "another utterance");
        draft.Refresh(store);
        Assert.Equal("", draft.Text);
        Assert.Single(draft.SelectedSegments);
    }

    [Fact]
    public void EmptySourceEntriesDoNotAddBlankCompositionSeparators()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, ""), Final(2, "first"), Final(3, ""), Final(4, "last"), Final(5, "")]);
        Assert.Equal("first" + Environment.NewLine + "last", draft.Text);
        Assert.Equal(5, draft.SelectedSegments.Count);
    }

    [Fact]
    public void NewSessionDoesNotInventFinalForSelectedClosedPartial()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "unfinished");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        store.BeginSession(NextSession);
        store.AddFinal(NextSession, AudioSource.Microphone, 1, "unselected new session", 0, 1);
        draft.Refresh(store);
        Assert.Equal("unfinished", draft.Text);
        Assert.True(draft.IsProvisional);
        Assert.False(Assert.Single(draft.SelectedSegments).IsActive);
        Assert.Equal(Session, draft.SelectedSegments[0].Key.CaptureSessionId);
        Assert.Null(draft.SelectedSegments[0].ProtocolSegmentId);
        Assert.Equal(2, draft.Revision);
    }

    [Fact]
    public void SelectedCopiesSurviveEvictionClearAndNewSession()
    {
        var store = Started(1);
        store.AddFinal(Session, AudioSource.Microphone, 1, "selected old final", 0, 1);
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var selected = draft.SelectedSegments;
        store.AddFinal(Session, AudioSource.Computer, 2, "evicts old final", 1, 2);
        draft.Refresh(store);
        Assert.Same(selected, draft.SelectedSegments);
        store.Clear();
        draft.Refresh(store);
        store.BeginSession(NextSession);
        store.AddFinal(NextSession, AudioSource.Microphone, 1, "new final ID one", 0, 1);
        draft.Refresh(store);
        Assert.Equal("selected old final", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(1, draft.Revision);
        Assert.Equal(Session, draft.Freeze().SourceRefs[0].Key.CaptureSessionId);
    }

    [Fact]
    public void EvictedUnfinishedCopyRemainsProvisionalAndIsNotRetargeted()
    {
        var store = Started(1);
        store.SetPartial(Session, AudioSource.Microphone, "selected guess");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var selected = draft.SelectedSegments;
        store.BeginSession(NextSession);
        store.SetPartial(NextSession, AudioSource.Microphone, "evicts old guess");
        draft.Refresh(store);
        Assert.Equal("selected guess", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.True(draft.IsProvisional);
        Assert.Equal(Session, draft.SelectedSegments[0].Key.CaptureSessionId);
    }

    [Fact]
    public void CrossSessionReplacementIsRejectedAtomically()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, "keep")]);
        draft.EditText("edited keep");
        var selected = draft.SelectedSegments;
        var revision = draft.Revision;
        var id = draft.QuestionId;
        var other = new TranscriptSegment(new(NextSession, AudioSource.Computer, 0, 1), 1, "other session", true, 1, 0, 1);
        Assert.Equal("segments", Assert.Throws<ArgumentException>(() => draft.ReplaceSelection([Final(2, "same session"), other])).ParamName);
        Assert.Equal("edited keep", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(revision, draft.Revision);
        Assert.Equal(id, draft.QuestionId);
        Assert.True(draft.UserEdited);
    }

    [Fact]
    public void SelectionNullInputsAreRejectedAtomically()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, "keep")]);
        var selected = draft.SelectedSegments;
        Assert.Equal("segments", Assert.Throws<ArgumentNullException>(() => draft.ReplaceSelection(null!)).ParamName);
        Assert.Equal("segments", Assert.Throws<ArgumentException>(() => draft.ReplaceSelection([Final(2, "x"), null!])).ParamName);
        Assert.Equal("keep", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(1, draft.Revision);
        Assert.Equal("store", Assert.Throws<ArgumentNullException>(() => draft.Refresh(null!)).ParamName);
    }

    [Fact]
    public void EnumerationFailureDoesNotPartiallyApplySelection()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, "keep")]);
        var selected = draft.SelectedSegments;
        Assert.Throws<InvalidOperationException>(() => draft.ReplaceSelection(FailingSelection()));
        Assert.Equal("keep", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(1, draft.Revision);
    }

    [Fact]
    public void OversizedSingleSelectionIsRejectedWithoutChangingEditedDraft()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, "keep original")]);
        draft.EditText("keep edit");
        var selected = draft.SelectedSegments;
        var revision = draft.Revision;
        var exception = Assert.Throws<ArgumentException>(
            () => draft.ReplaceSelection([Final(2, new string('x', QuestionDraft.MaximumCharacters + 1))]));
        Assert.Equal("segments", exception.ParamName);
        Assert.Contains("8192", exception.Message);
        Assert.Equal("keep edit", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.True(draft.UserEdited);
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public void MultiSegmentCeilingIncludesExactSeparatorsAndRejectsAtomically()
    {
        var draft = new QuestionDraft();
        var first = Final(1, new string('a', 4_000));
        var second = Final(2, new string('b', QuestionDraft.MaximumCharacters - 4_000 - Environment.NewLine.Length));
        draft.ReplaceSelection([first, second]);
        Assert.Equal(QuestionDraft.MaximumCharacters, draft.Text.Length);
        var selected = draft.SelectedSegments;
        var text = draft.Text;
        Assert.Throws<ArgumentException>(() => draft.ReplaceSelection([first, second, Final(3, "x")]));
        Assert.Equal(text, draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(1, draft.Revision);
        Assert.Equal(text, draft.Freeze().ExactText);
    }

    [Fact]
    public void EditCeilingAndNullRejectionsAreAtomic()
    {
        var draft = new QuestionDraft();
        draft.ReplaceSelection([Final(1, "keep")]);
        var selected = draft.SelectedSegments;
        Assert.Equal("text", Assert.Throws<ArgumentNullException>(() => draft.EditText(null!)).ParamName);
        Assert.Equal("text", Assert.Throws<ArgumentException>(
            () => draft.EditText(new string('x', QuestionDraft.MaximumCharacters + 1))).ParamName);
        Assert.Equal("keep", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.False(draft.UserEdited);
        Assert.Equal(1, draft.Revision);
        var maximum = new string('x', QuestionDraft.MaximumCharacters);
        draft.EditText(maximum);
        Assert.Equal(maximum, draft.Text);
        Assert.Equal(maximum, draft.Freeze().ExactText);
    }

    [Fact]
    public void OversizedFinalRefreshKeepsPreviousDraftInspectable()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "reviewed short partial");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var selected = draft.SelectedSegments;
        var id = draft.QuestionId;
        store.AddFinal(Session, AudioSource.Microphone, 1, new string('x', QuestionDraft.MaximumCharacters + 1), 0, 1);
        var exception = Assert.Throws<InvalidOperationException>(() => draft.Refresh(store));
        Assert.Contains("8192", exception.Message);
        Assert.Contains("unchanged", exception.Message);
        Assert.Equal("reviewed short partial", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.True(draft.IsProvisional);
        Assert.False(draft.UserEdited);
        Assert.Equal(1, draft.Revision);
        Assert.Equal(id, draft.QuestionId);
        Assert.Equal("reviewed short partial", draft.Freeze().ExactText);
        draft.EditText("explicit shorter question");
        draft.Refresh(store);
        Assert.Equal("explicit shorter question", draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
    }

    [Fact]
    public void MultiSegmentOversizedRefreshIsAtomicAndCanRecoverOnShorterRevision()
    {
        var store = Started();
        store.AddFinal(Session, AudioSource.Computer, 1, new string('a', 4_000), 0, 1);
        store.SetPartial(Session, AudioSource.Microphone, "short");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        var text = draft.Text;
        var selected = draft.SelectedSegments;
        store.SetPartial(Session, AudioSource.Microphone, new string('b', 4_200));
        Assert.Throws<InvalidOperationException>(() => draft.Refresh(store));
        Assert.Equal(text, draft.Text);
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(1, draft.Revision);
        store.SetPartial(Session, AudioSource.Microphone, "shorter recovery");
        draft.Refresh(store);
        Assert.EndsWith("shorter recovery", draft.Text);
        Assert.Equal(2, draft.Revision);
        Assert.Equal(3, draft.SelectedSegments[1].Revision);
    }

    [Fact]
    public void SelectionCountIsBoundedEvenWhenAllSourceTextsAreEmpty()
    {
        var draft = new QuestionDraft();
        var maximum = Enumerable.Range(1, QuestionDraft.MaximumSelectedSegments).Select(id => Final(id, "")).ToArray();
        draft.ReplaceSelection(maximum);
        Assert.Equal(QuestionDraft.MaximumSelectedSegments, draft.SelectedSegments.Count);
        Assert.Equal("", draft.Text);
        var selected = draft.SelectedSegments;
        Assert.Throws<ArgumentException>(() => draft.ReplaceSelection(maximum.Append(Final(257, ""))));
        Assert.Same(selected, draft.SelectedSegments);
        Assert.Equal(1, draft.Revision);
        draft.ReplaceSelection(maximum.Concat(maximum));
        Assert.Equal(QuestionDraft.MaximumSelectedSegments, draft.SelectedSegments.Count);
        Assert.Equal(1, draft.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("\u2003\u00a0")]
    public void EmptyOrWhitespaceDraftCannotFreeze(string text)
    {
        var draft = new QuestionDraft();
        draft.EditText(text);
        Assert.Equal(text, draft.Text);
        Assert.Throws<InvalidOperationException>(() => draft.Freeze());
    }

    [Fact]
    public void ClearInvalidatesQuestionIdentityAndPreventsLateRefreshResurrection()
    {
        var store = Started();
        store.SetPartial(Session, AudioSource.Microphone, "old question");
        var draft = new QuestionDraft();
        draft.ReplaceSelection(store.Segments);
        draft.EditText("edited");
        var id = draft.QuestionId;
        draft.Clear();
        Assert.NotEqual(id, draft.QuestionId);
        Assert.NotEqual(Guid.Empty, draft.QuestionId);
        Assert.Equal(0, draft.Revision);
        Assert.Equal("", draft.Text);
        Assert.Empty(draft.SelectedSegments);
        Assert.False(draft.UserEdited);
        Assert.False(draft.IsProvisional);
        store.AddFinal(Session, AudioSource.Microphone, 1, "late final", 0, 1);
        draft.Refresh(store);
        Assert.Empty(draft.SelectedSegments);
        Assert.Equal("", draft.Text);
        var clearedId = draft.QuestionId;
        draft.Clear();
        Assert.NotEqual(clearedId, draft.QuestionId);
    }

    private static IEnumerable<TranscriptSegment> FailingSelection()
    {
        yield return Final(2, "uncommitted");
        throw new InvalidOperationException("Synthetic enumerator failure.");
    }

    private static TranscriptSegment Final(int id, string text) =>
        new(new(Session, AudioSource.Microphone, 0, id), 1, text, true, id, null, null);

    private static StructuredTranscriptStore Started(int maximumSegments = 256)
    {
        var store = new StructuredTranscriptStore(maximumSegments);
        store.BeginSession(Session);
        return store;
    }
}
