using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SQLExtended.Statistics.Capture;
using StatisticsParser.Core.Models;
using StatisticsParser.Core.Parsing;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.Statistics;

/// <summary>
/// The capture → parse → render pipeline, shared by <see cref="StatisticsCommand"/> (which activates the tool window)
/// and the tool window's own "Re-parse" button (which does not steal focus).
/// </summary>
internal static class StatisticsPresenter
{
    /// <summary>Fire-and-forget entry point. Never throws into the caller; failures land in the status line and the ActivityLog.</summary>
    public static void Show(AsyncPackage package, bool activate)
    {
        if (package == null) return;
        _ = package.JoinableTaskFactory.RunAsync(async () => await ShowAsync(package, activate));
    }

    private static async Task ShowAsync(AsyncPackage package, bool activate)
    {
        try
        {
            var capture = await MessagesTabReader.GetMessagesTextAsync(package, package.DisposalToken);

            // Parsing is pure CPU over a potentially large string — keep it off the UI thread.
            ParseResult parsed = null;
            if (capture.Status == MessagesCaptureStatus.Ok)
            {
                string text = capture.Text;
                parsed = await Task.Run(() => Parser.ParseData(text, StatisticsOptions.ResolveLanguage(text), StatisticsOptions.SuppressZeroColumns));
            }

            var control = await GetControlAsync(package, activate);
            if (control == null) return;

            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            control.SetPackage(package);
            if (capture.Status == MessagesCaptureStatus.Ok)
                control.Render(capture.Text, parsed);
            else
                control.ShowCaptureStatus(capture);

            if (capture.Error != null)
                ActivityLogHelper.LogError(package, "SQLExtended Statistics", $"Messages capture ({capture.Status}): {capture.Error}");
        }
        catch (Exception ex)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            ActivityLogHelper.LogError(package, "SQLExtended Statistics", $"Parse Statistics failed: {ex}");
            System.Diagnostics.Debug.WriteLine($"[SQLExtended] Parse Statistics failed: {ex}");
        }
    }

    /// <summary>
    /// Resolves the tool window's control, creating and showing the window when <paramref name="activate"/> is set.
    /// Re-parse passes false so it refreshes in place — and returns null if the window has since been closed.
    /// </summary>
    private static async System.Threading.Tasks.Task<StatisticsControl> GetControlAsync(AsyncPackage package, bool activate)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        var window = activate
            ? await package.ShowToolWindowAsync(typeof(StatisticsToolWindow), 0, create: true, package.DisposalToken)
            : await package.FindToolWindowAsync(typeof(StatisticsToolWindow), 0, create: false, package.DisposalToken);

        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (activate && window?.Frame is IVsWindowFrame frame)
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(frame.Show());

        return (window as StatisticsToolWindow)?.Control;
    }
}
