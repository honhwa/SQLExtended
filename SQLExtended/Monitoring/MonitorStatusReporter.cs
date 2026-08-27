using System;
using System.Threading;
using Microsoft.VisualStudio.Threading;

namespace SQLExtended.Monitoring;

/// <summary>
/// Marshals a collection's progress from the worker thread it runs on to the status line. Constructed on the UI
/// thread by the control and passed down into the query service, which reports one step per section.
///
/// <para>It lives apart from <see cref="MonitorPlan"/> so that file stays free of the Visual Studio threading
/// assembly and the test project can link it — the same split as <c>ExportFileNaming</c> and
/// <c>EncryptedModuleCrypto</c>.</para>
/// </summary>
/// <remarks>
/// Deliberately not <see cref="System.Progress{T}"/>. That captures <c>SynchronizationContext.Current</c> at
/// construction and silently falls back to the thread pool when there is none, which here would mean touching a
/// <c>TextBlock</c> off the UI thread — a crash that would appear only wherever the context happened to be
/// missing, which is not something worth discovering in the field. Going through the JoinableTaskFactory is
/// explicit and correct from any thread.
/// </remarks>
internal sealed class MonitorStatusReporter : IProgress<MonitorStep>
{
    private readonly JoinableTaskFactory _factory;
    private readonly CancellationToken _cancellation;
    private readonly Action<string> _write;

    public MonitorStatusReporter(JoinableTaskFactory factory, CancellationToken cancellation, Action<string> write)
    {
        _factory = factory;
        _cancellation = cancellation;
        _write = write;
    }

    public void Report(MonitorStep step)
    {
        // A cancelled poll's remaining reports would otherwise land on the status line of whatever the window is
        // doing next. Nothing on that line is worth making the collection wait for, so this stays fire-and-forget:
        // the switches queue on the dispatcher in the order they were made, and the completed poll's own status —
        // written synchronously once it is back on the UI thread — lands after all of them.
        if (_cancellation.IsCancellationRequested) return;

        _factory.RunAsync(async () =>
        {
            try
            {
                await _factory.SwitchToMainThreadAsync(_cancellation);
                _write(step.Text);
            }
            catch (OperationCanceledException) { }
        }).Task.Forget();
    }
}
