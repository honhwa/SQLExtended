using Newtonsoft.Json;

namespace SQLExtended.Updates;

/// <summary>
/// Schema of the version.json document the release pipeline publishes alongside the .vsix.
/// Example:
/// {
///   "version": "2026.5.14.1430",
///   "url":     "https://github.com/JamTheRadar/SQLExtended/releases/download/v2026.8.0.1/SQLExtended-2026.8.0.1.vsix",
///   "notes":   "Fixed completion icons; added history retention setting.",
///   "minRequiredVersion": "2026.1.1.0"
/// }
/// </summary>
public sealed class VersionManifest
{
    [JsonProperty("version")]
    public string Version { get; set; }

    [JsonProperty("url")]
    public string Url { get; set; }

    [JsonProperty("notes")]
    public string Notes { get; set; }

    /// <summary>Optional. If set and the running version is below this, the update is presented as required (not skippable).</summary>
    [JsonProperty("minRequiredVersion")]
    public string MinRequiredVersion { get; set; }
}
