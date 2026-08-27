namespace SQLExtended.Cache.Models;

/// <summary>
/// Represents the current state of the schema cache for a specific database.
/// </summary>
internal enum CacheState
{
    NotLoaded,
    Loading,
    Ready,
    Stale,
    Error
}
