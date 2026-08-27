using Newtonsoft.Json;

namespace SQLExtended.ScriptLibrary.Models;

/// <summary>
/// A single reusable T-SQL script in the library. Curated (built-in) scripts ship embedded
/// in the assembly and are read-only; user scripts live in %APPDATA%\SQLExtended\SSMS\script-library.json.
/// </summary>
public sealed class LibraryScript
{
    /// <summary>Stable identifier. Built-in scripts use "builtin:{Category}/{Name}"; user scripts use a GUID.</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public string Category { get; set; } = "General";

    public string Description { get; set; } = "";

    public string Body { get; set; } = "";

    /// <summary>True for curated scripts shipped with the extension. Set at load time, not persisted to the user file.</summary>
    [JsonIgnore]
    public bool IsBuiltIn { get; set; }
}
