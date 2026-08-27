using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace SQLExtended.IntelliSense;

/// <summary>
/// VS ImageMoniker constants for SQL object types in the completion list.
/// Uses built-in VS image catalog icons.
/// </summary>
internal static class CompletionIcons
{
    public static ImageMoniker Table => KnownMonikers.Table;
    public static ImageMoniker View => KnownMonikers.View;
    public static ImageMoniker StoredProcedure => KnownMonikers.StoredProcedure;
    public static ImageMoniker ScalarFunction => KnownMonikers.Method;
    public static ImageMoniker TableFunction => KnownMonikers.TableFunction;
    public static ImageMoniker Synonym => KnownMonikers.Reference;
    public static ImageMoniker TableType => KnownMonikers.FlatList;

    // Parameter icon
    public static ImageMoniker Parameter => KnownMonikers.Parameter;

    // Keyword and snippet icons
    public static ImageMoniker Keyword => KnownMonikers.IntellisenseKeyword;
    public static ImageMoniker Snippet => KnownMonikers.Snippet;

    // Database icon (for USE completion)
    public static ImageMoniker Database => KnownMonikers.Database;

    // Schema icon (for cross-database three-part name completion)
    public static ImageMoniker Schema => KnownMonikers.Namespace;

    // Function-argument value icons (data type / datepart suggestions)
    public static ImageMoniker DataType => KnownMonikers.Type;
    public static ImageMoniker DatePart => KnownMonikers.Constant;

    // DBCC command icon
    public static ImageMoniker DbccCommand => KnownMonikers.Console;

    // Collation name icon (COLLATE completion)
    public static ImageMoniker Collation => KnownMonikers.Localize;

    // Column icons
    public static ImageMoniker Column => KnownMonikers.DatabaseColumn;
    public static ImageMoniker PrimaryKey => KnownMonikers.Key;
    public static ImageMoniker ForeignKey => KnownMonikers.Reference;
    public static ImageMoniker Identity => KnownMonikers.Counter;
    public static ImageMoniker ComputedColumn => KnownMonikers.Property;

    /// <summary>
    /// Returns the appropriate icon for a SQL Server object type code.
    /// </summary>
    public static ImageMoniker ForObjectType(string objectType) => objectType?.Trim() switch
    {
        "U" => Table,
        "V" => View,
        "P" => StoredProcedure,
        "FN" => ScalarFunction,
        "IF" => TableFunction,
        "TF" => TableFunction,
        "SN" => Synonym,
        "TT" => TableType,
        _ => KnownMonikers.DatabaseColumn
    };

    /// <summary>
    /// Returns the appropriate icon for a column based on its properties.
    /// Priority: PK > FK > Identity > Computed > Column.
    /// </summary>
    public static ImageMoniker ForColumn(bool isPrimaryKey, bool isForeignKey, bool isIdentity, bool isComputed)
    {
        if (isPrimaryKey) return PrimaryKey;
        if (isForeignKey) return ForeignKey;
        if (isIdentity) return Identity;
        if (isComputed) return ComputedColumn;
        return Column;
    }
}
