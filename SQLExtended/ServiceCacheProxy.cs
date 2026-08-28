using System;
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
    /// The reading of it - including the Entra token an Azure sign-in carries instead of a password - lives in
    /// <see cref="UIConnectionInfoReader"/>, which all three harvest strategies share.
    /// </summary>
    public static string BuildConnectionString(object uiConnInfo) => UIConnectionInfoReader.BuildConnectionString(uiConnInfo, "SQLExtended for SSMS");
}
