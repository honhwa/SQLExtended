using System;

namespace SQLExtended.Cache.Models;

internal sealed class CacheRefreshEventArgs : EventArgs
{
    public string ConnectionKey { get; }
    public string DatabaseName { get; }
    public CacheState NewState { get; }
    public int ObjectCount { get; }

    public CacheRefreshEventArgs(string connectionKey, string databaseName, CacheState newState, int objectCount)
    {
        ConnectionKey = connectionKey;
        DatabaseName = databaseName;
        NewState = newState;
        ObjectCount = objectCount;
    }
}
