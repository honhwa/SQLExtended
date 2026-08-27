using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Search;

/// <summary>
/// Dockable tool window for SQL Search. Hosts <see cref="SqlSearchControl"/>.
/// Registered via <see cref="ProvideToolWindowAttribute"/> on the package.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F60001")]
public sealed class SqlSearchToolWindow : ToolWindowPane
{
    public SqlSearchToolWindow() : base(null)
    {
        Caption = "SQL Search";
        Content = new SqlSearchControl();
    }
}
