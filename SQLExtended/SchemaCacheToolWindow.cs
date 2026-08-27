using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended;

/// <summary>
/// Dockable tool window showing what the shared schema cache currently holds, per server.
/// Hosts <see cref="SchemaCacheControl"/>. Registered via <see cref="ProvideToolWindowAttribute"/> on the package.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F60002")]
public sealed class SchemaCacheToolWindow : ToolWindowPane
{
    public SchemaCacheToolWindow() : base(null)
    {
        Caption = "Schema Cache";
        Content = new SchemaCacheControl();
    }
}
