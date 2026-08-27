using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SQLExtended.Formatting;

/// <summary>
/// Manages named formatter profiles stored in formatter-profiles.json.
/// On first use, migrates the existing formatter-options.json into a "Default" profile.
/// </summary>
public class FormatterProfileManager
{
    public const string DefaultProfileName = "Default";

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS");

    private static readonly string ProfilesPath =
        Path.Combine(SettingsDir, "formatter-profiles.json");

    private static readonly string LegacyOptionsPath =
        Path.Combine(SettingsDir, "formatter-options.json");

    private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Newtonsoft.Json.Formatting.Indented,
        Converters = { new StringEnumConverter() },
        DefaultValueHandling = DefaultValueHandling.Include
    };

    private static FormatterProfileManager _instance;
    private static readonly object Lock = new object();

    public string ActiveProfileName { get; set; } = DefaultProfileName;
    public Dictionary<string, FormatterOptions> Profiles { get; set; } = new Dictionary<string, FormatterOptions>(StringComparer.OrdinalIgnoreCase);

    public static FormatterProfileManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (Lock)
                {
                    if (_instance == null)
                        _instance = Load();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Force a reload from disk (e.g. after external changes).
    /// </summary>
    public static void Reload()
    {
        lock (Lock)
        {
            _instance = Load();
        }
    }

    /// <summary>
    /// Returns the options for the active profile.
    /// </summary>
    public FormatterOptions GetActiveOptions()
    {
        if (Profiles.TryGetValue(ActiveProfileName, out var options))
            return options.Clone();

        // Fallback: if active profile was deleted, return Default or new
        if (Profiles.TryGetValue(DefaultProfileName, out var defaults))
            return defaults.Clone();

        return new FormatterOptions();
    }

    /// <summary>
    /// Returns just the keyword/identifier casing of the active profile, read directly from the
    /// in-memory profile with no clone or disk access. Cheap enough for per-keystroke completion
    /// building; reflects live edits since <see cref="SaveProfile"/> mutates the same dictionary.
    /// </summary>
    public (CasingOption Keyword, CasingOption Identifier) GetActiveCasing()
    {
        var options = Profiles.TryGetValue(ActiveProfileName, out var active) ? active
                    : Profiles.TryGetValue(DefaultProfileName, out var def) ? def
                    : null;

        return options == null
            ? (CasingOption.Unchanged, CasingOption.Unchanged)
            : (options.KeywordCase, options.IdentifierCase);
    }

    /// <summary>
    /// Returns all profile names in sorted order, with "Default" always first.
    /// </summary>
    public List<string> GetProfileNames()
    {
        var names = Profiles.Keys.ToList();
        names.Sort(StringComparer.OrdinalIgnoreCase);

        // Ensure Default is always first
        if (names.Remove(DefaultProfileName))
            names.Insert(0, DefaultProfileName);

        return names;
    }

    /// <summary>
    /// Saves or updates a named profile with the given options.
    /// </summary>
    public void SaveProfile(string name, FormatterOptions options)
    {
        Profiles[name] = options.Clone();
        Save();
    }

    /// <summary>
    /// Sets the active profile by name.
    /// </summary>
    public void SetActiveProfile(string name)
    {
        if (!Profiles.ContainsKey(name))
            return;

        ActiveProfileName = name;
        Save();

        // Also write to legacy path so FormatCommand.ExecuteFormat() stays fast
        WriteLegacyOptions(Profiles[name]);
    }

    /// <summary>
    /// Deletes a profile. Cannot delete "Default".
    /// </summary>
    public bool DeleteProfile(string name)
    {
        if (string.Equals(name, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Profiles.Remove(name))
            return false;

        if (string.Equals(ActiveProfileName, name, StringComparison.OrdinalIgnoreCase))
        {
            ActiveProfileName = DefaultProfileName;
            if (Profiles.TryGetValue(DefaultProfileName, out var defaults))
                WriteLegacyOptions(defaults);
        }

        Save();
        return true;
    }

    /// <summary>
    /// Renames a profile. Cannot rename "Default".
    /// </summary>
    public bool RenameProfile(string oldName, string newName)
    {
        if (string.Equals(oldName, DefaultProfileName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(newName))
            return false;

        if (Profiles.ContainsKey(newName))
            return false;

        if (!Profiles.TryGetValue(oldName, out var options))
            return false;

        Profiles.Remove(oldName);
        Profiles[newName] = options;

        if (string.Equals(ActiveProfileName, oldName, StringComparison.OrdinalIgnoreCase))
            ActiveProfileName = newName;

        Save();
        return true;
    }

    /// <summary>
    /// Creates a new profile by cloning the given options. Returns false if the name already exists.
    /// </summary>
    public bool CreateProfile(string name, FormatterOptions options)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (Profiles.ContainsKey(name))
            return false;

        Profiles[name] = options.Clone();
        Save();
        return true;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var data = new ProfilesData
            {
                ActiveProfile = ActiveProfileName,
                Profiles = Profiles
            };
            string json = JsonConvert.SerializeObject(data, JsonSettings);
            File.WriteAllText(ProfilesPath, json);
        }
        catch
        {
            // Best effort
        }
    }

    private static FormatterProfileManager Load()
    {
        var manager = new FormatterProfileManager();

        try
        {
            if (File.Exists(ProfilesPath))
            {
                string json = File.ReadAllText(ProfilesPath);
                var data = JsonConvert.DeserializeObject<ProfilesData>(json, JsonSettings);
                if (data != null)
                {
                    manager.ActiveProfileName = data.ActiveProfile ?? DefaultProfileName;
                    if (data.Profiles != null)
                    {
                        manager.Profiles = new Dictionary<string, FormatterOptions>(
                            data.Profiles, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
        }
        catch
        {
            // Corrupted — start fresh
        }

        // Ensure Default profile always exists
        if (!manager.Profiles.ContainsKey(DefaultProfileName))
        {
            // Migrate from legacy formatter-options.json if it exists
            FormatterOptions legacyOptions = null;
            try
            {
                if (File.Exists(LegacyOptionsPath))
                {
                    string json = File.ReadAllText(LegacyOptionsPath);
                    legacyOptions = JsonConvert.DeserializeObject<FormatterOptions>(json, JsonSettings);
                }
            }
            catch
            {
                // Ignore
            }

            manager.Profiles[DefaultProfileName] = legacyOptions ?? new FormatterOptions();
        }

        // Ensure active profile exists
        if (!manager.Profiles.ContainsKey(manager.ActiveProfileName))
            manager.ActiveProfileName = DefaultProfileName;

        return manager;
    }

    private static void WriteLegacyOptions(FormatterOptions options)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonConvert.SerializeObject(options, JsonSettings);
            File.WriteAllText(LegacyOptionsPath, json);
        }
        catch
        {
            // Best effort
        }
    }

    private class ProfilesData
    {
        public string ActiveProfile { get; set; }
        public Dictionary<string, FormatterOptions> Profiles { get; set; }
    }
}
