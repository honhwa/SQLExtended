using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;

namespace SQLExtended.ScriptLibrary;

/// <summary>
/// Dockable tool window for the SQLExtended Script Library. Hosts <see cref="ScriptLibraryControl"/>.
/// </summary>
[Guid("D4A1C2E3-5B6F-4A8D-9C1E-2F3A4B5C6D7E")]
public sealed class ScriptLibraryToolWindow : ToolWindowPane
{
    public ScriptLibraryToolWindow() : base(null)
    {
        Caption = "Script Library";
        Content = new ScriptLibraryControl();
    }
}
