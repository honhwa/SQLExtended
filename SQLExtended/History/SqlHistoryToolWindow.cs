using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.History;

/// <summary>
/// Dockable tool window for SQL Tab History.
/// </summary>
[Guid("C8F2A4B6-3D7E-4F9A-B5C8-D7E1F2A3B4C5")]
public sealed class SqlHistoryToolWindow : ToolWindowPane
{
    public SqlHistoryToolWindow() : base(null)
    {
        Caption = "SQL History";
        Content = new SqlHistoryControl();
    }
}
