using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Handles Ctrl+Space (Edit.CompleteWord / Edit.ListMembers) on SQL buffers by
/// explicitly opening the async completion broker. Required because SSMS's legacy
/// IntelliSense intercepts the command before it reaches the default handler.
/// </summary>
[Export(typeof(ICommandHandler))]
[ContentType("SQL")]
[Name("SQLExtended SQL Invoke Completion")]
[Order(Before = "Default Completion Command Handlers")]
internal sealed class SqlInvokeCompletionCommandHandler :
    ICommandHandler<InvokeCompletionListCommandArgs>,
    ICommandHandler<CommitUniqueCompletionListItemCommandArgs>
{
    [Import]
    internal IAsyncCompletionBroker Broker { get; set; }

    public string DisplayName => "SQLExtended SQL Invoke Completion";

    public CommandState GetCommandState(InvokeCompletionListCommandArgs args)
        => CommandState.Available;

    public bool ExecuteCommand(InvokeCompletionListCommandArgs args, CommandExecutionContext context)
        => TriggerCompletion(args.TextView, args.SubjectBuffer);

    public CommandState GetCommandState(CommitUniqueCompletionListItemCommandArgs args)
        => CommandState.Available;

    public bool ExecuteCommand(CommitUniqueCompletionListItemCommandArgs args, CommandExecutionContext context)
        => TriggerCompletion(args.TextView, args.SubjectBuffer);

    private bool TriggerCompletion(
        Microsoft.VisualStudio.Text.Editor.ITextView textView,
        Microsoft.VisualStudio.Text.ITextBuffer subjectBuffer)
    {
        try
        {
            if (Broker == null)
                return false;

            // If a session is already open, don't create a new one — let the default behavior apply
            var existing = Broker.GetSession(textView);
            if (existing != null)
                return false;

            var caret = textView.Caret.Position.Point.GetPoint(subjectBuffer, Microsoft.VisualStudio.Text.PositionAffinity.Predecessor);
            if (!caret.HasValue)
                return false;

            var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, caret.Value.Snapshot, '\0');
            var session = Broker.TriggerCompletion(textView, trigger, caret.Value, default);

            if (session != null)
            {
                SqlCompletionSource.DebugLog("[CommandHandler] Ctrl+Space triggered async completion session");
                return true; // command handled
            }
        }
        catch (Exception ex)
        {
            SqlCompletionSource.DebugLog($"[CommandHandler] Ctrl+Space failed: {ex.Message}");
        }

        return false;
    }
}
