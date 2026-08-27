using System;
using System.Data.SqlClient;
using System.Reflection;

namespace SQLExtended;

/// <summary>
/// Reflection-based proxy for Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache.
/// SqlPackageBase.dll provides ServiceCache but references VS v18 assemblies that conflict
/// with our v17 NuGet SDK at compile time. At runtime in SSMS, the correct versions are loaded.
/// This proxy accesses ServiceCache entirely via reflection to avoid the compile-time CS1705 error.
/// </summary>
internal static class ServiceCacheProxy
{
    private static Type _serviceCacheType;
    private static bool _initialized;

    private static Type GetServiceCacheType()
    {
        if (_initialized) return _serviceCacheType;
        _initialized = true;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name.Equals("SqlPackageBase", StringComparison.OrdinalIgnoreCase))
            {
                _serviceCacheType = asm.GetType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache");
                return _serviceCacheType;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo.UIConnectionInfo
    /// and builds a connection string from it.
    /// </summary>
    public static string GetActiveConnectionFromServiceCache()
    {
        try
        {
            var scType = GetServiceCacheType();
            if (scType == null) return null;

            // ServiceCache.ScriptFactory
            var sfProp = scType.GetProperty("ScriptFactory",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var scriptFactory = sfProp?.GetValue(null);
            if (scriptFactory == null) return null;

            // ScriptFactory.CurrentlyActiveWndConnectionInfo
            var cawiProp = scriptFactory.GetType().GetProperty("CurrentlyActiveWndConnectionInfo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var connInfoWrapper = cawiProp?.GetValue(scriptFactory);
            if (connInfoWrapper == null) return null;

            // .UIConnectionInfo
            var uiConnProp = connInfoWrapper.GetType().GetProperty("UIConnectionInfo",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var uiConnInfo = uiConnProp?.GetValue(connInfoWrapper);
            if (uiConnInfo == null) return null;

            return BuildConnectionString(uiConnInfo);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets ServiceCache.ServiceProvider and uses it to resolve a service by type.
    /// Returns null if ServiceProvider is not available.
    /// </summary>
    public static object GetService(Type serviceType)
    {
        try
        {
            var scType = GetServiceCacheType();
            if (scType == null) return null;

            var spProp = scType.GetProperty("ServiceProvider",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var sp = spProp?.GetValue(null);
            if (sp == null) return null;

            // IServiceProvider.GetService(Type)
            var getServiceMethod = sp.GetType().GetMethod("GetService",
                BindingFlags.Instance | BindingFlags.Public,
                null, new[] { typeof(Type) }, null);

            return getServiceMethod?.Invoke(sp, new object[] { serviceType });
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Builds a SqlClient connection string from a UIConnectionInfo object via reflection.
    /// </summary>
    public static string BuildConnectionString(object uiConnInfo)
    {
        var type = uiConnInfo.GetType();

        string server = GetProp(type, uiConnInfo, "ServerName")
                     ?? GetProp(type, uiConnInfo, "ServerNameNoDot");

        string database = GetIndexedProp(type, uiConnInfo, "AdvancedOptions", "DATABASE")
                       ?? GetProp(type, uiConnInfo, "DatabaseName")
                       ?? "master";

        string userName = GetProp(type, uiConnInfo, "UserName");
        string password = GetProp(type, uiConnInfo, "Password");

        if (string.IsNullOrEmpty(server))
            return null;

        var builder = new SqlConnectionStringBuilder
        {
            // ADMIN: stripped — see ConnectionHelper.NormalizeHarvestedDataSource. Harvesting a DAC window's
            // server name verbatim would run the whole schema cache over the instance's single
            // administrator connection, and leave a pooled one holding it afterwards.
            DataSource = ConnectionHelper.NormalizeHarvestedDataSource(server),
            InitialCatalog = database,
            TrustServerCertificate = true,
            ConnectTimeout = 10,
            ApplicationName = "SQLExtended for SSMS"
        };

        // Check authentication type
        string authValue = GetProp(type, uiConnInfo, "AuthenticationType");
        if (authValue != null && authValue.Contains("Sql"))
        {
            builder.IntegratedSecurity = false;
            builder.UserID = userName ?? "";
            if (!string.IsNullOrEmpty(password))
                builder.Password = password;
            else
                builder.IntegratedSecurity = true;
        }
        else
        {
            builder.IntegratedSecurity = string.IsNullOrEmpty(password);
            if (!builder.IntegratedSecurity)
            {
                builder.UserID = userName ?? "";
                builder.Password = password;
            }
        }

        return builder.ConnectionString;
    }

    private static string GetProp(Type type, object obj, string propName)
    {
        try
        {
            var prop = type.GetProperty(propName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return prop?.GetValue(obj)?.ToString();
        }
        catch { return null; }
    }

    private static string GetIndexedProp(Type type, object obj, string collectionProp, string key)
    {
        try
        {
            var prop = type.GetProperty(collectionProp,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var collection = prop?.GetValue(obj);
            if (collection == null) return null;

            // Use the specific string-keyed indexer to avoid AmbiguousMatchException
            // when the collection has multiple indexers (e.g., Item[string] and Item[int]).
            var collType = collection.GetType();
            var indexer = collType.GetProperty("Item",
                BindingFlags.Instance | BindingFlags.Public,
                null, typeof(string), new[] { typeof(string) }, null);

            if (indexer != null)
                return indexer.GetValue(collection, new object[] { key })?.ToString();

            // Fallback: try finding the indexer by scanning all "Item" properties
            foreach (var p in collType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (p.Name != "Item") continue;
                var parameters = p.GetIndexParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    return p.GetValue(collection, new object[] { key })?.ToString();
            }

            return null;
        }
        catch { return null; }
    }
}
