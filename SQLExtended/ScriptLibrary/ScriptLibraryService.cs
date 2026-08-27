using Newtonsoft.Json;
using SQLExtended.ScriptLibrary.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SQLExtended.ScriptLibrary;

/// <summary>
/// Singleton store for the script library. Merges read-only curated scripts (embedded JSON manifest)
/// with editable user scripts persisted to %APPDATA%\SQLExtended\SSMS\script-library.json.
/// </summary>
public sealed class ScriptLibraryService
{
    public static ScriptLibraryService Instance { get; } = new ScriptLibraryService();

    private const string CuratedResourceName = "SQLExtended.ScriptLibrary.CuratedScripts.json";

    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS");

    private static readonly string StorePath = Path.Combine(StoreDir, "script-library.json");

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    private readonly object _gate = new object();
    private List<LibraryScript> _curated = new List<LibraryScript>();
    private List<LibraryScript> _user = new List<LibraryScript>();
    private bool _initialized;

    /// <summary>Raised whenever the user script set changes (add/update/delete).</summary>
    public event EventHandler Changed;

    /// <summary>Loads the curated manifest and the user file. Safe to call repeatedly; only runs once.</summary>
    public void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            _curated = LoadCurated();
            _user = LoadUser();
            _initialized = true;
        }
    }

    /// <summary>All scripts (curated + user), ordered by category then name.</summary>
    public IReadOnlyList<LibraryScript> All()
    {
        EnsureInit();
        lock (_gate)
            return _curated.Concat(_user)
                .OrderBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>Scripts whose name, category, description, or body contains <paramref name="term"/> (case-insensitive).</summary>
    public IReadOnlyList<LibraryScript> Query(string term)
    {
        var all = All();
        if (string.IsNullOrWhiteSpace(term)) return all;

        return all.Where(s =>
            Contains(s.Name, term) ||
            Contains(s.Category, term) ||
            Contains(s.Description, term) ||
            Contains(s.Body, term)).ToList();
    }

    /// <summary>Distinct category names across all scripts.</summary>
    public IReadOnlyList<string> Categories()
    {
        EnsureInit();
        lock (_gate)
            return _curated.Concat(_user)
                .Select(s => string.IsNullOrWhiteSpace(s.Category) ? "General" : s.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    /// <summary>Adds a new user script or updates an existing one (matched by Id). Built-in scripts cannot be saved.</summary>
    public void AddOrUpdateUser(LibraryScript script)
    {
        if (script == null) throw new ArgumentNullException(nameof(script));
        if (script.IsBuiltIn) throw new InvalidOperationException("Built-in scripts are read-only.");

        EnsureInit();
        lock (_gate)
        {
            if (string.IsNullOrEmpty(script.Id))
                script.Id = Guid.NewGuid().ToString("N");

            int idx = _user.FindIndex(s => s.Id == script.Id);
            if (idx >= 0) _user[idx] = script;
            else _user.Add(script);

            SaveUser();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Deletes a user script by Id. No-op for built-in or unknown ids.</summary>
    public void DeleteUser(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        EnsureInit();
        lock (_gate)
        {
            int removed = _user.RemoveAll(s => s.Id == id);
            if (removed == 0) return;
            SaveUser();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    // --- internals ---

    private void EnsureInit()
    {
        if (!_initialized) Initialize();
    }

    private static bool Contains(string haystack, string needle) =>
        haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static List<LibraryScript> LoadCurated()
    {
        try
        {
            var asm = typeof(ScriptLibraryService).Assembly;
            using var stream = asm.GetManifestResourceStream(CuratedResourceName);
            if (stream == null) return new List<LibraryScript>();
            using var reader = new StreamReader(stream);
            var manifest = JsonConvert.DeserializeObject<CuratedManifest>(reader.ReadToEnd());
            var scripts = manifest?.Scripts ?? new List<LibraryScript>();
            foreach (var s in scripts)
            {
                s.IsBuiltIn = true;
                if (string.IsNullOrWhiteSpace(s.Category)) s.Category = "General";
                s.Id = $"builtin:{s.Category}/{s.Name}";
            }
            return scripts;
        }
        catch
        {
            return new List<LibraryScript>();
        }
    }

    private static List<LibraryScript> LoadUser()
    {
        try
        {
            if (!File.Exists(StorePath)) return new List<LibraryScript>();
            var json = File.ReadAllText(StorePath);
            var list = JsonConvert.DeserializeObject<List<LibraryScript>>(json) ?? new List<LibraryScript>();
            foreach (var s in list)
            {
                s.IsBuiltIn = false;
                if (string.IsNullOrEmpty(s.Id)) s.Id = Guid.NewGuid().ToString("N");
                if (string.IsNullOrWhiteSpace(s.Category)) s.Category = "General";
            }
            return list;
        }
        catch
        {
            return new List<LibraryScript>();
        }
    }

    private void SaveUser()
    {
        try
        {
            Directory.CreateDirectory(StoreDir);
            File.WriteAllText(StorePath, JsonConvert.SerializeObject(_user, JsonSettings));
        }
        catch
        {
            // Best effort — matches the rest of the extension's settings persistence.
        }
    }

    private sealed class CuratedManifest
    {
        public int Version { get; set; } = 1;
        public List<LibraryScript> Scripts { get; set; } = new List<LibraryScript>();
    }
}
