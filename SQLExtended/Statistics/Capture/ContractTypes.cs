// -----------------------------------------------------------------------------
// Adapted from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Vsix/Capture/ContractTypes.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md.
// -----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;

namespace SQLExtended.Statistics.Capture;

/// <summary>
/// Late-bound reflection cache for the SSMS-shipped BrokeredContracts assembly, which exposes the query editor's
/// Results/Messages panes as brokered services. The DLL ships with SSMS and cannot be redistributed by this VSIX
/// (same rule as the other SSMS internals), so it is loaded from the SSMS install at runtime and every type, method
/// and property is looked up by name. Initialization runs once per process; the result — success or failure — is
/// cached by the <see cref="AsyncLazy{T}"/> wrapper.
///
/// Every lookup failure throws a message naming exactly what moved, so an SSMS minor-version drift shows up as a
/// diagnosable status line rather than a silent empty pane.
/// </summary>
internal sealed class ContractTypes
{
    public const string ContractsDllName = "Microsoft.SqlServer.Management.UI.VSIntegration.SqlEditor.BrokeredContracts.dll";

    private const string Ns = "Microsoft.SqlServer.Management.UI.VSIntegration.BrokeredServices";

    private static readonly AsyncLazy<ContractTypes> _instance = new(LoadAsync, joinableTaskFactory: null);

    public static Task<ContractTypes> GetAsync(CancellationToken ct) => _instance.GetValueAsync(ct);

    public Assembly ContractsAssembly { get; private set; }
    public Type IQueryEditorTabDataServiceBrokered { get; private set; }
    public Type ISqlEditorServiceBrokered { get; private set; }
    public Type TextContentSegmentType { get; private set; }
    public Type QueryResultsPaneType { get; private set; }
    public Type QueryResultsPaneInfoType { get; private set; }
    public Type SqlEditorConnectionDetailsType { get; private set; }
    public object QueryEditorTabDataServiceMoniker { get; private set; }
    public object SqlEditorServiceMoniker { get; private set; }

    public MethodInfo GetMessagesTabSegmentAsyncMethod { get; private set; }
    public MethodInfo GetAvailablePanesAsyncMethod { get; private set; }
    public MethodInfo GetCurrentConnectionAsyncMethod { get; private set; }

    public PropertyInfo TextContentSegment_Content { get; private set; }
    public PropertyInfo TextContentSegment_StartPosition { get; private set; }
    public PropertyInfo TextContentSegment_TotalLength { get; private set; }
    public PropertyInfo QueryResultsPaneInfo_PaneType { get; private set; }
    public PropertyInfo SqlEditorConnectionDetails_EditorMoniker { get; private set; }
    public PropertyInfo SqlEditorConnectionDetails_IsActive { get; private set; }
    public object QueryResultsPane_Messages { get; private set; }

    private static Task<ContractTypes> LoadAsync()
    {
        // Initialize() is purely CPU-bound (Assembly.LoadFrom + reflection), so synchronous work wrapped in
        // Task.FromResult is fine here — no main-thread or RPC calls.
        var types = new ContractTypes();
        types.Initialize();
        return Task.FromResult(types);
    }

    private void Initialize()
    {
        ContractsAssembly = Assembly.LoadFrom(ResolveContractsDllPath());

        IQueryEditorTabDataServiceBrokered = RequireType("IQueryEditorTabDataServiceBrokered");
        ISqlEditorServiceBrokered = RequireType("ISqlEditorServiceBrokered");
        TextContentSegmentType = RequireType("TextContentSegment");
        QueryResultsPaneType = RequireType("QueryResultsPane");
        QueryResultsPaneInfoType = RequireType("QueryResultsPaneInfo");
        SqlEditorConnectionDetailsType = RequireType("SqlEditorConnectionDetails");
        var tabDataDescriptorsType = RequireType("QueryEditorTabDataServiceDescriptors");
        var editorServiceDescriptorsType = RequireType("SqlEditorBrokeredServiceDescriptors");

        // SSMS 22.6+ added a leading `string editorMoniker` parameter to every IQueryEditorTabDataServiceBrokered
        // method; the moniker is fetched from the sibling ISqlEditorServiceBrokered.GetCurrentConnectionAsync.
        GetMessagesTabSegmentAsyncMethod = ResolveMethod(IQueryEditorTabDataServiceBrokered, "GetMessagesTabSegmentAsync",
            requiredPositionalTypes: new[] { typeof(string), typeof(int), typeof(int) }, requiresCancellationToken: true);
        GetAvailablePanesAsyncMethod = ResolveMethod(IQueryEditorTabDataServiceBrokered, "GetAvailablePanesAsync",
            requiredPositionalTypes: new[] { typeof(string) }, requiresCancellationToken: true);
        GetCurrentConnectionAsyncMethod = ResolveMethod(ISqlEditorServiceBrokered, "GetCurrentConnectionAsync",
            requiredPositionalTypes: Type.EmptyTypes, requiresCancellationToken: true);

        TextContentSegment_Content = RequireProperty(TextContentSegmentType, "Content");
        TextContentSegment_StartPosition = RequireProperty(TextContentSegmentType, "StartPosition");
        TextContentSegment_TotalLength = RequireProperty(TextContentSegmentType, "TotalLength");
        QueryResultsPaneInfo_PaneType = RequireProperty(QueryResultsPaneInfoType, "PaneType");
        SqlEditorConnectionDetails_EditorMoniker = RequireProperty(SqlEditorConnectionDetailsType, "EditorMoniker");
        SqlEditorConnectionDetails_IsActive = RequireProperty(SqlEditorConnectionDetailsType, "IsActive");

        QueryResultsPane_Messages = Enum.Parse(QueryResultsPaneType, "Messages");
        QueryEditorTabDataServiceMoniker = ResolveMonikerFrom(tabDataDescriptorsType, "QueryEditorTabDataService");
        SqlEditorServiceMoniker = ResolveMonikerFrom(editorServiceDescriptorsType, "SqlEditorService");
    }

    /// <summary>
    /// Locates the contracts DLL. It normally sits beside Ssms.exe (the AppDomain base directory), but a second copy
    /// ships under Extensions\Microsoft\SSMSAgent, so that is probed as a fallback in case the layout changes.
    /// </summary>
    private static string ResolveContractsDllPath()
    {
        string ideDir = AppDomain.CurrentDomain.BaseDirectory ?? "";

        var candidates = new List<string>
        {
            Path.Combine(ideDir, ContractsDllName),
            Path.Combine(ideDir, "Extensions", "Microsoft", "SSMSAgent", ContractsDllName)
        };

        foreach (string candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        throw new FileNotFoundException("SSMS BrokeredContracts assembly not found. Probed: " + string.Join("; ", candidates), candidates[0]);
    }

    private Type RequireType(string simpleName)
    {
        string full = Ns + "." + simpleName;
        var t = ContractsAssembly.GetType(full, throwOnError: false);
        if (t == null)
            throw new InvalidOperationException("BrokeredContracts type not found: " + full + " (SSMS minor version may have moved it)");
        return t;
    }

    /// <summary>
    /// Among all overloads with the given name, picks the one whose parameter list begins with the required positional
    /// types (in order), optionally contains a CancellationToken, and whose extra parameters are all defaultable. This
    /// survives an SSMS minor version adding new optional parameters to the brokered method.
    /// </summary>
    private static MethodInfo ResolveMethod(Type type, string name, Type[] requiredPositionalTypes, bool requiresCancellationToken)
    {
        var candidates = type.GetMethods().Where(m => m.Name == name).ToList();
        if (candidates.Count == 0)
            throw new InvalidOperationException("BrokeredContracts method not found: " + type.FullName + "." + name);

        MethodInfo best = null;
        int bestExtras = int.MaxValue;
        foreach (var m in candidates)
        {
            var ps = m.GetParameters();
            if (ps.Length < requiredPositionalTypes.Length) continue;

            bool prefixOk = true;
            for (int i = 0; i < requiredPositionalTypes.Length; i++)
                if (ps[i].ParameterType != requiredPositionalTypes[i]) { prefixOk = false; break; }
            if (!prefixOk) continue;

            if (requiresCancellationToken && !ps.Skip(requiredPositionalTypes.Length).Any(p => p.ParameterType == typeof(CancellationToken)))
                continue;

            bool extrasOk = true;
            foreach (var p in ps.Skip(requiredPositionalTypes.Length))
            {
                if (p.ParameterType == typeof(CancellationToken)) continue;
                if (!p.HasDefaultValue && !p.ParameterType.IsValueType) { extrasOk = false; break; }
            }
            if (!extrasOk) continue;

            int extras = ps.Length - requiredPositionalTypes.Length - (requiresCancellationToken ? 1 : 0);
            if (extras < bestExtras) { best = m; bestExtras = extras; }
        }

        if (best == null)
            throw new InvalidOperationException("BrokeredContracts method not found: " + type.FullName + "." + name + " with required prefix ["
                + string.Join(", ", requiredPositionalTypes.Select(t => t.Name)) + "]" + (requiresCancellationToken ? " + CancellationToken" : ""));
        return best;
    }

    private static PropertyInfo RequireProperty(Type type, string name)
    {
        var p = type.GetProperty(name);
        if (p == null)
            throw new InvalidOperationException("BrokeredContracts property not found: " + type.FullName + "." + name);
        return p;
    }

    private static object ResolveMonikerFrom(Type descriptorsType, string memberName)
    {
        const BindingFlags bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        // Try the known member name first.
        var prop = descriptorsType.GetProperty(memberName, bf);
        if (prop != null && prop.GetGetMethod(true)?.IsStatic == true)
        {
            var v = prop.GetValue(null);
            if (v != null) return v;
        }
        var field = descriptorsType.GetField(memberName, bf);
        if (field != null && field.IsStatic)
        {
            var v = field.GetValue(null);
            if (v != null) return v;
        }

        // Fallback: any moniker- or descriptor-shaped static member on the type.
        foreach (var p in descriptorsType.GetProperties(bf))
        {
            if (p.GetGetMethod(true)?.IsStatic != true) continue;
            if (!IsMonikerShape(p.PropertyType)) continue;
            var v = p.GetValue(null);
            if (v != null) return v;
        }
        foreach (var f in descriptorsType.GetFields(bf))
        {
            if (!f.IsStatic) continue;
            if (!IsMonikerShape(f.FieldType)) continue;
            var v = f.GetValue(null);
            if (v != null) return v;
        }

        throw new InvalidOperationException("Could not locate " + descriptorsType.Name + "." + memberName + " moniker");
    }

    private static bool IsMonikerShape(Type t) =>
        t != null && (t.Name.IndexOf("ServiceMoniker", StringComparison.Ordinal) >= 0
                      || (t.FullName ?? string.Empty).IndexOf("ServiceRpcDescriptor", StringComparison.Ordinal) >= 0);
}
