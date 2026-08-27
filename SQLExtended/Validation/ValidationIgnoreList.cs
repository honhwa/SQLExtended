using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SQLExtended.Validation;

/// <summary>
/// User-managed list of referenced objects and databases to exclude from validation results.
/// Matching (<see cref="IsIgnored"/>) is pure and case-insensitive so it can be unit tested without
/// touching disk. Persisted to %APPDATA%\SQLExtended\SSMS\validation-ignores.json.
/// </summary>
public sealed class ValidationIgnoreList
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS");

    private static readonly string SettingsPath =
        Path.Combine(SettingsDir, "validation-ignores.json");

    /// <summary>Referenced database names to ignore entirely (e.g. an optional/central DB).</summary>
    public List<string> Databases { get; set; } = new();

    /// <summary>Referenced objects to ignore, stored as "schema.entity" (e.g. "dbo.SqlServerVersions").</summary>
    public List<string> Objects { get; set; } = new();

    [JsonIgnore]
    public int Count => Databases.Count + Objects.Count;

    /// <summary>
    /// True when a reference to the given target should be hidden. Matches either the referenced
    /// database, or the "schema.entity" of the referenced object — both case-insensitive.
    /// </summary>
    public bool IsIgnored(string referencedDatabase, string referencedSchema, string referencedEntity)
    {
        if (!string.IsNullOrEmpty(referencedDatabase) &&
            Databases.Any(d => string.Equals(d, referencedDatabase, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!string.IsNullOrEmpty(referencedEntity))
        {
            string key = ObjectKey(referencedSchema, referencedEntity);
            if (Objects.Any(o => string.Equals(o, key, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    /// <summary>Builds the "schema.entity" key used for object matching (schema defaults to dbo).</summary>
    public static string ObjectKey(string schema, string entity)
        => (string.IsNullOrEmpty(schema) ? "dbo" : schema) + "." + entity;

    public bool AddDatabase(string database)
    {
        if (string.IsNullOrWhiteSpace(database) ||
            Databases.Any(d => string.Equals(d, database, StringComparison.OrdinalIgnoreCase)))
            return false;
        Databases.Add(database);
        return true;
    }

    public bool AddObject(string schema, string entity)
    {
        if (string.IsNullOrWhiteSpace(entity)) return false;
        string key = ObjectKey(schema, entity);
        if (Objects.Any(o => string.Equals(o, key, StringComparison.OrdinalIgnoreCase)))
            return false;
        Objects.Add(key);
        return true;
    }

    public void Remove(string entry)
    {
        Databases.RemoveAll(d => string.Equals(d, entry, StringComparison.OrdinalIgnoreCase));
        Objects.RemoveAll(o => string.Equals(o, entry, StringComparison.OrdinalIgnoreCase));
    }

    public void Clear()
    {
        Databases.Clear();
        Objects.Clear();
    }

    public static ValidationIgnoreList Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<ValidationIgnoreList>(json) ?? new ValidationIgnoreList();
            }
        }
        catch
        {
            // Corrupted file — return an empty list.
        }
        return new ValidationIgnoreList();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best effort.
        }
    }
}
