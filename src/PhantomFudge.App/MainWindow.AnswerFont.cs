using System.Windows;
using System.Windows.Automation;
using PhantomFudge.Core;

namespace PhantomFudge.App;

public partial class MainWindow
{
    private int? chosenAnswerFontSize;
    private bool updatingAnswerFont;

    private void AnswerFontSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> args)
    {
        if (!answerViewReady || updatingAnswerFont)
            return;
        SetAnswerFontSize((int)Math.Round(args.NewValue));
    }

    private void SetAnswerFontSize(int size)
    {
        if (size < KeyboardCommands.MinimumFontSize || size > KeyboardCommands.MaximumFontSize)
            throw new ArgumentOutOfRangeException(nameof(size), "Answer font size must be from8to24.");
        chosenAnswerFontSize = size;
        ApplyAnswerFontSize(size);
        UpdateWorkspaceLayout();
    }

    private void ApplyAnswerFontSize(int automaticSize)
    {
        var size = chosenAnswerFontSize ?? automaticSize;
        updatingAnswerFont = true;
        try
        {
            AnswerText.FontSize = size;
            if (AnswerFontSlider.Value != size)
                AnswerFontSlider.Value = size;
        }
        finally { updatingAnswerFont = false; }
    }

    private void AdjustAnswerFont(int delta) =>
        SetAnswerFontSize(Math.Clamp((int)AnswerText.FontSize + delta,
            KeyboardCommands.MinimumFontSize, KeyboardCommands.MaximumFontSize));

    private void UpdateAnswerActionLabels()
    {
        if (!answerViewReady)
            return;
        var compact = QuestionPanel.ActualWidth < 560;
        PreviousAnswerToggle.Width = compact ? 80 : 118;
        QuestionEditButton.Content = questionEditing ? compact ? "Done" : "Done typing"
            : AwaitingClarificationReply ? compact ? "Reply" : "Type reply" : compact ? "Edit" : "Type / edit";
        QuestionCopyButton.Content = compact ? "Copy" : AwaitingClarificationReply ? "Copy original" : "Copy draft";
        AutomationProperties.SetName(QuestionEditButton, questionEditing ? "Done typing"
            : AwaitingClarificationReply ? "Type clarification reply" : "Type or edit the question");
        AutomationProperties.SetName(QuestionCopyButton, AwaitingClarificationReply ? "Copy original question" : "Copy question draft");
        var preparing = agencyPreparation is { IsCompleted: false } && !AnswerBusy;
        var interpretation = answerRun?.Request.Clarification is { Confirmed: false };
        AnswerCancelButton.Content = preparing ? compact ? "Cancel prep" : "Cancel preparation" : compact ? "Cancel" : "Cancel answer";
        AnswerCopyButton.Content = compact ? "Copy" : interpretation ? "Copy interpretation" : "Copy answer";
        AutomationProperties.SetName(AnswerCancelButton, preparing ? "Cancel provider preparation" : "Cancel current answer");
        AutomationProperties.SetName(AnswerCopyButton, interpretation ? "Copy interpretation" : "Copy answer");
    }
}
