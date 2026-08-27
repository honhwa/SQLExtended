namespace SQLExtended.Cache.Models;

internal sealed class CachedDatabase
{
    public string Name { get; set; }
    public string State { get; set; }
    public int CompatibilityLevel { get; set; }
    public string RecoveryModel { get; set; }
}
