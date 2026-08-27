using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended;

/// <summary>
/// Command handler for Ctrl+Shift+D — grabs the object name under the cursor,
/// queries the database for schema info, and shows it in a dialog.
/// </summary>
internal sealed class SchemaViewerCommand
{
    // Must match the .vsct command IDs (or use dynamic command registration)
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new Guid("a1b2c3d4-e5f6-7890-abcd-123456789abc");

    private readonly AsyncPackage _package;
    private static SchemaViewerCommand _instance;

    private SchemaViewerCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));

        var menuCommandId = new CommandID(CommandSet, CommandId);
        var menuItem = new MenuCommand(Execute, menuCommandId);
        commandService.AddCommand(menuItem);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
        {
            _instance = new SchemaViewerCommand(package, commandService);
        }
    }

    /// <summary>
    /// Main entry point when user presses Ctrl+Shift+D
    /// </summary>
    private void Execute(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            // 1. Get the word under the cursor (or selected text)
            string objectName = EditorHelper.GetObjectNameAtCursor(_package);
            if (string.IsNullOrWhiteSpace(objectName))
            {
                ShowMessage("Schema Viewer", "Place your cursor on a table or view name, or select the name.");
                return;
            }

            // 2. Get the active SQL connection from the current query window
            string connectionString = ConnectionHelper.GetActiveConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                ShowMessage("Schema Viewer", "No active database connection found. Open a connected query window first.");
                return;
            }

            // 3. Query the schema on a background thread, then show the dialog back on the UI thread.
            //
            // Not an optimisation. Building the script can open a second connection — for a module defined
            // WITH ENCRYPTION it opens a dedicated administrator connection and briefly ALTERs the object
            // (see ModuleDecryptionService) — and doing that inline would freeze SSMS for as long as it
            // takes, with no window on screen to say why. The decryption service refuses to run on the UI
            // thread at all, so inline would also mean encrypted objects never decrypt here.
            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                string schemaScript = null;
                Exception failure = null;

                try
                {
                    schemaScript = await Task.Run(() => SchemaQueryService.GetSchemaScript(connectionString, objectName));
                }
                catch (Exception ex)
                {
                    failure = ex;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                if (failure != null)
                    ShowMessage("Schema Viewer - Error", failure.Message);
                else if (string.IsNullOrWhiteSpace(schemaScript))
                    ShowMessage("Schema Viewer", $"Object '{objectName}' not found in the current database.");
                else
                    new SchemaDialog(objectName, schemaScript, connectionString).ShowDialog();
            });
        }
        catch (Exception ex)
        {
            ShowMessage("Schema Viewer - Error", ex.Message);
        }
    }

    private void ShowMessage(string title, string message)
    {
        VsShellUtilities.ShowMessageBox(
            _package, message, title,
            OLEMSGICON.OLEMSGICON_INFO,
            OLEMSGBUTTON.OLEMSGBUTTON_OK,
            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
    }
}
