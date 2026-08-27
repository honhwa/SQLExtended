using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Validation;

/// <summary>
/// Dockable tool window for Schema Validation. Hosts <see cref="SchemaValidationControl"/>.
/// Registered via <see cref="ProvideToolWindowAttribute"/> on the package.
/// </summary>
[Guid("B7E3F1A2-9C4D-4E8F-A1B2-C3D4E5F60003")]
public sealed class SchemaValidationToolWindow : ToolWindowPane
{
    public SchemaValidationToolWindow() : base(null)
    {
        Caption = "Schema Validation";
        Content = new SchemaValidationControl();
    }
}
