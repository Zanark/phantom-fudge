using PhantomFudge.Core.Answers;

namespace PhantomFudge.Core.Tests;

public sealed class AnswerClarificationTests
{
    [Fact]
    public void HistoryIsAnImmutableSnapshotOfOnlyTheExplicitExchange()
    {
        var original = new ClarificationTurn("  Noisy question\r\n", "An interpretation 🙂");
        var source = new List<ClarificationTurn> { original };
        var clarification = new AnswerClarification(false, source);
        source[0] = new("Changed question", "Changed interpretation");
        source.Clear();

        Assert.False(clarification.Confirmed);
        Assert.Equal(original, Assert.Single(clarification.History));
        Assert.NotSame(original, clarification.History[0]);
        var list = Assert.IsAssignableFrom<IList<ClarificationTurn>>(clarification.History);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list[0] = new("Other", "Other"));
        Assert.Throws<NotSupportedException>(() => list.Add(new("Other", "Other")));
        Assert.Throws<NotSupportedException>(list.Clear);
        clarification.ValidateFor("  correction\r\n");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void InitialAndCorrectionRequestsPermitExactlyFourInterpretationRounds(int completedTurns)
    {
        var clarification = new AnswerClarification(false, Turns(completedTurns));
        clarification.ValidateFor("Current question or correction");
        Assert.Equal(completedTurns, clarification.History.Count);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EachCompletedInterpretationCanBeExplicitlyConfirmed(int completedTurns)
    {
        var clarification = new AnswerClarification(true, Turns(completedTurns));
        clarification.ValidateFor("Yes");
        Assert.True(clarification.Confirmed);
    }

    [Fact]
    public void ConfirmationNeedsAnInterpretationAndAFifthInterpretationIsRejected()
    {
        AssertCode("clarification_stage", () => new AnswerClarification(true, []));
        AssertCode("clarification_round_limit", () => new AnswerClarification(false, Turns(4)));
        AssertCode("clarification_history_limit", () => new AnswerClarification(true, Turns(5)));
        AssertCode("clarification_history_limit", () => new AnswerClarification(false, Turns(5)));
    }

    [Fact]
    public void HistoryEnumerationIsBoundedRatherThanMaterializingAnUnboundedSource()
    {
        var enumerated = 0;
        IEnumerable<ClarificationTurn> Endless()
        {
            while (true)
            {
                enumerated++;
                if (enumerated > 5) throw new InvalidOperationException("Enumeration was not bounded.");
                yield return new("Question", "Interpretation");
            }
        }

        AssertCode("clarification_history_limit", () => new AnswerClarification(true, Endless()));
        Assert.Equal(5, enumerated);
    }

    [Fact]
    public void MissingHistoryAndNullTurnsAreSafeFailures()
    {
        AssertCode("clarification_history", () => new AnswerClarification(false, null!));
        AssertCode("clarification_history", () => new AnswerClarification(false, [null!]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void EveryTextFieldMustBeNonblank(string? text)
    {
        AssertCode("clarification_text", () => new AnswerClarification(false, [new(text!, "Interpretation")]));
        AssertCode("clarification_text", () => new AnswerClarification(false, [new("Question", text!)]));
        AssertCode("clarification_text", () => new AnswerClarification(false, []).ValidateFor(text!));
    }

    [Fact]
    public void EachTextFieldRejectsMoreThan8192Utf16UnitsWithoutTruncating()
    {
        Assert.Equal(4, AnswerClarification.MaximumHistoryTurns);
        Assert.Equal(32_768, AnswerClarification.MaximumContextCharacters);
        var maximum = new string('x', QuestionDraft.MaximumCharacters);
        var oversized = maximum + "x";
        new AnswerClarification(false, [new(maximum, maximum)]).ValidateFor(maximum);
        AssertCode("clarification_text_limit", () => new AnswerClarification(false, [new(oversized, "Interpretation")]));
        AssertCode("clarification_text_limit", () => new AnswerClarification(false, [new("Question", oversized)]));
        AssertCode("clarification_text_limit", () => new AnswerClarification(false, []).ValidateFor(oversized));
    }

    [Fact]
    public void CombinedHistoryAndCurrentTextHaveAnIndependentInclusiveBudget()
    {
        var full = new string('u', 8192);
        var half = new string('i', 4096);
        var clarification = new AnswerClarification(false, [new(full, full), new(half, half)]);
        clarification.ValidateFor(full);
        Assert.Equal(full, clarification.History[0].UserText);

        var overBudget = new AnswerClarification(false, [new(full, full), new(half, half + "i")]);
        AssertCode("clarification_context_limit", () => overBudget.ValidateFor(full));
        var noRoomForConfirmation = new AnswerClarification(true, [new(full, full), new(full, full)]);
        AssertCode("clarification_context_limit", () => noRoomForConfirmation.ValidateFor("Y"));
        AssertCode("clarification_context_limit", () => new AnswerClarification(true, [new(full, full), new(full, full), new("Q", "I")]));
    }

    [Fact]
    public void UnicodeIsPreservedAndLimitsCountUtf16NotEncodedBytes()
    {
        var maximum = string.Concat(Enumerable.Repeat("🙂", 4096));
        var clarification = new AnswerClarification(false, [new(maximum, maximum)]);
        clarification.ValidateFor(maximum);
        Assert.Equal(8192, clarification.History[0].UserText.Length);
        Assert.Equal(maximum, clarification.History[0].Interpretation);
        AssertCode("clarification_text_limit", () => clarification.ValidateFor(maximum + "🙂"));
    }

    [Fact]
    public void MalformedUnicodeInAnyFieldIsRejectedWithoutEchoingContent()
    {
        foreach (var invalid in new[] { "\ud800", "\udfff", "\ud800x", "\udc00\ud800", "private-sentinel\ud800" })
        {
            AssertCode("clarification_unicode", () => new AnswerClarification(false, [new(invalid, "Interpretation")]));
            AssertCode("clarification_unicode", () => new AnswerClarification(false, [new("Question", invalid)]));
            AssertCode("clarification_unicode", () => new AnswerClarification(false, []).ValidateFor(invalid));
        }
    }

    private static IEnumerable<ClarificationTurn> Turns(int count) =>
        Enumerable.Range(0, count).Select(index => new ClarificationTurn("Question " + index, "Interpretation " + index));

    private static void AssertCode(string code, Action action)
    {
        var error = Assert.Throws<AnswerProviderException>(action);
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("private-sentinel", error.Message);
    }
}
