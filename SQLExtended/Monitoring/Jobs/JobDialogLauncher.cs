using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using System.Xml;

namespace SQLExtended.Monitoring.Jobs;

/// <summary>
/// Opens SSMS's own Job Properties dialog for a job, so double-clicking a row in the dashboard lands in the
/// same editor as double-clicking the job in Object Explorer.
///
/// There is no public API for this. The path was worked out from SSMS's own Object Explorer menu definition:
/// <c>ObjectExplorer.dll</c>'s embedded <c>sqlexplorermenuitems.xml</c> declares the Job node's default action as
/// <code>
///   &lt;Object name='JobProperties' base='PropertiesItem'&gt;
///     &lt;Property Name='Assembly'&gt;SqlManagerUi.dll&lt;/Property&gt;
///     &lt;Property Name='Type'&gt;Microsoft.SqlServer.Management.SqlManagerUI.JobPropertySheet&lt;/Property&gt;
/// </code>
/// so the recipe is: build a <c>CDataContainer</c> (public, in SqlMgmt.dll) carrying the job's URN, hand it to
/// <c>JobPropertySheet</c>'s public <c>ctor(CDataContainer)</c>, and host the resulting control — it derives from
/// <c>SqlMgmtTreeViewControl</c>, which implements <c>ISqlControlCollection</c> — in <c>LaunchForm</c>, whose
/// <c>ctor(ISqlControlCollection, IServiceProvider)</c> is also public.
///
/// Everything is resolved by name at runtime rather than through assembly references, for the same reason
/// <c>Statistics/Capture/ContractTypes</c> does: these are undocumented internals, and a servicing update that
/// moves one of them should cost this one menu item, not the build. Every failure surfaces as a message on the
/// dashboard's status line — never an exception into SSMS.
/// </summary>
internal static class JobDialogLauncher
{
    private const string SqlMgmtAssembly = "SqlMgmt";
    private const string SqlManagerUIAssembly = "SqlManagerUI";
    private const string DataContainerType = "Microsoft.SqlServer.Management.SqlMgmt.CDataContainer";
    private const string LaunchFormType = "Microsoft.SqlServer.Management.SqlMgmt.LaunchForm";

    // LaunchForm rejects a plain IServiceProvider: "Host service provider MUST implement ILaunchFormHost".
    // LaunchFormHost is SSMS's own adapter for exactly that — public, implements ILaunchFormHost (…2 through 5)
    // *and* IServiceProvider, with a public ctor that wraps the provider you hand it. Note it lives in SqlMgmt.dll
    // despite the ObjectExplorer namespace.
    private const string LaunchFormHostType = "Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer.LaunchFormHost";
    private const string ControlCollectionType = "Microsoft.SqlServer.Management.SqlMgmt.ISqlControlCollection";
    private const string JobSheetType = "Microsoft.SqlServer.Management.SqlManagerUI.JobPropertySheet";
    private const string ConnectionInfoAssembly = "Microsoft.SqlServer.ConnectionInfo";

    // SqlConnectionInfoWithConnection, not its SqlConnectionInfo base: CDataContainer's (ServerType, object, bool)
    // ctor casts the connection object to the derived type, so passing the base throws InvalidCastException. The
    // derived type inherits every property set below and has a public parameterless ctor, so it costs nothing and
    // satisfies the cast whichever way CDataContainer decides to read the object.
    private const string ConnectionInfoType = "Microsoft.SqlServer.Management.Common.SqlConnectionInfoWithConnection";

    /// <summary>
    /// Shows the Job Properties dialog modally. Must be called on the UI thread.
    /// </summary>
    /// <param name="provider">The package — LaunchForm needs a service provider to reach the shell.</param>
    /// <param name="serverName">
    /// The name SMO knows the instance by, i.e. what <c>SERVERPROPERTY('ServerName')</c> returned. Not the
    /// connection string's Data Source: through an availability-group listener or a CNAME the two differ, and it
    /// is the SMO name that has to appear in the URN for the job to resolve.
    /// </param>
    /// <param name="jobId">
    /// <c>sysjobs.job_id</c>. This is what actually puts the dialog into edit mode — see
    /// <see cref="BuildFormDescription"/>. <see cref="Guid.Empty"/> falls back to the name.
    /// </param>
    /// <param name="jobName">The job's name.</param>
    /// <param name="connectionString">The dashboard's msdb connection, reused for the dialog's own connection.</param>
    /// <param name="ownerHwnd">The shell's dialog owner window, so the dialog centres and stacks correctly.</param>
    public static void ShowJobProperties(IServiceProvider provider, string serverName, Guid jobId, string jobName, string connectionString, IntPtr ownerHwnd)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (string.IsNullOrWhiteSpace(serverName)) throw new InvalidOperationException("The server name is not known yet — refresh first.");
        if (string.IsNullOrWhiteSpace(jobName)) throw new InvalidOperationException("No job selected.");

        var sqlMgmt = ResolveAssembly(SqlMgmtAssembly);
        var sqlManagerUI = ResolveAssembly(SqlManagerUIAssembly);

        var containerType = RequireType(sqlMgmt, DataContainerType);
        var launchFormType = RequireType(sqlMgmt, LaunchFormType);
        var controlCollection = RequireType(sqlMgmt, ControlCollectionType);
        var sheetType = RequireType(sqlManagerUI, JobSheetType);

        object container = Step("building the data container", () => CreateDataContainer(containerType, serverName, jobId, jobName, connectionString));

        // JobPropertySheet(CDataContainer). The parameterless ctor exists too but produces an unbound sheet.
        var sheetCtor = sheetType.GetConstructor(new[] { containerType })
            ?? throw new InvalidOperationException("JobPropertySheet has no CDataContainer constructor in this SSMS build.");
        object sheet = Step("constructing JobPropertySheet", () => sheetCtor.Invoke(new[] { container }));

        if (!controlCollection.IsInstanceOfType(sheet))
            throw new InvalidOperationException("JobPropertySheet is not an ISqlControlCollection in this SSMS build.");

        var formCtor = launchFormType.GetConstructor(new[] { controlCollection, typeof(IServiceProvider) })
            ?? throw new InvalidOperationException("LaunchForm has no (ISqlControlCollection, IServiceProvider) constructor in this SSMS build.");

        // LaunchForm demands an ILaunchFormHost, and — verified the hard way — passing SSMS's LaunchFormHost
        // directly is still rejected even though it implements that interface. The check is a service *query*
        // (provider.GetService(typeof(ILaunchFormHost))), not a cast, so the host has to be served by the
        // provider rather than be the provider. HostServiceProvider below does that.
        var hostType = RequireType(sqlMgmt, LaunchFormHostType);
        var hostCtor = hostType.GetConstructor(new[] { typeof(IServiceProvider) })
            ?? throw new InvalidOperationException("LaunchFormHost has no (IServiceProvider) constructor in this SSMS build.");
        object host = Step("constructing LaunchFormHost", () => hostCtor.Invoke(new object[] { provider }));

        var form = (Form)Step("constructing LaunchForm", () => formCtor.Invoke(new[] { sheet, new HostServiceProvider(host, provider) }));

        // LaunchForm never copies a caption off the hosted control in its constructor — the sheet pushes one
        // through ILaunchForm.Caption during InitializeUI — so set it explicitly rather than rely on that
        // ordering. (The "New Job" caption this used to show was JobPropertySheet.Init doing
        // Text = JobSR.NewJob because JobData had fallen into DialogMode.Create; see BuildFormDescription.)
        form.Text = "Job Properties - " + jobName;

        using (form)
        {
            Step("showing the dialog", () =>
            {
                if (ownerHwnd != IntPtr.Zero)
                    form.ShowDialog(new ShellOwner(ownerHwnd));
                else
                    form.ShowDialog();
                return (object)null;
            });
        }
    }

    /// <summary>
    /// Runs one reflection step and, on failure, reports what was being attempted along with the *real* error.
    ///
    /// Without this every failure in here arrives as "Exception has been thrown by the target of an invocation"
    /// — <see cref="TargetInvocationException"/>'s own message, which says nothing about the actual problem and
    /// hides which of the four steps failed. The inner exception is rethrown with its stack intact so the
    /// ActivityLog entry still points at the real frame.
    /// </summary>
    private static T Step<T>(string what, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new InvalidOperationException($"{Innermost(ex.InnerException).Message} (while {what})", ex.InnerException);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{Innermost(ex).Message} (while {what})", ex);
        }
    }

    /// <summary>The deepest exception in the chain — the one that actually describes what went wrong.</summary>
    internal static Exception Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex;
    }

    /// <summary>
    /// The whole exception chain as one line, type names included.
    ///
    /// This goes on the dashboard rather than only into the ActivityLog because the ActivityLog is only written
    /// when SSMS is launched with <c>/log</c> — so for anyone running SSMS normally it is not there when the
    /// failure happens. Reflection into SSMS internals fails in ways only the inner exception explains
    /// (a certificate rejection, a missing member after a servicing update), so the chain has to be visible.
    /// </summary>
    internal static string DescribeChain(Exception ex)
    {
        var parts = new List<string>();
        for (var current = ex; current != null; current = current.InnerException)
            parts.Add(current.GetType().Name + ": " + current.Message);

        return string.Join("  <- ", parts);
    }

    /// <summary>
    /// Builds the container SSMS's properties dialogs expect: a SQL server connection plus a params document
    /// whose &lt;urn&gt; names the object being edited.
    ///
    /// The connection is handed over as a fully populated <c>SqlConnectionInfo</c> rather than through
    /// CDataContainer's simpler <c>(serverName, trusted, user, password)</c> constructor, because that
    /// constructor keeps only those four values and SMO then rebuilds the connection with its own defaults.
    /// <see cref="ConnectionHelper"/> deliberately sets <c>TrustServerCertificate=true</c> — SSMS 22 encrypts by
    /// default and self-signed certificates are the norm on internal instances — and losing that made the
    /// dialog's own connect fail certificate validation on servers the dashboard itself had just read happily.
    /// Only primitives cross into SSMS's assemblies here, so there is no version-identity risk.
    /// </summary>
    private static object CreateDataContainer(Type containerType, string serverName, Guid jobId, string jobName, string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);

        // SqlConnectionInfo expresses integrated and SQL logins. An Entra sign-in reaches our own connections as
        // the access token EntraTokenBroker holds, which this dialog has no property to receive - and such a
        // connection string carries no credentials at all, so without this check the sheet would open on a
        // connection that cannot log in.
        if (EntraTokenBroker.HasToken(builder.DataSource))
        {
            throw new NotSupportedException("Opening the Job Properties dialog is not supported for Entra (Azure AD) authentication. "
                                          + "Use Object Explorer for this connection.");
        }

        if (builder.Authentication != SqlAuthenticationMethod.NotSpecified && builder.Authentication != SqlAuthenticationMethod.SqlPassword)
        {
            throw new NotSupportedException($"Opening the Job Properties dialog is not supported for {builder.Authentication} authentication. "
                                          + "Use Object Explorer for this connection.");
        }

        object connectionInfo = CreateConnectionInfo(builder, serverName);

        var serverTypeEnum = containerType.GetNestedType("ServerType")
            ?? throw new InvalidOperationException("CDataContainer.ServerType is missing in this SSMS build.");
        object sqlServerType = Enum.Parse(serverTypeEnum, "SQL");

        // ownConnection: true — a plain SqlConnectionInfo describes a connection rather than being a live one
        // (that is SqlConnectionInfoWithConnection's job), so the container has to open its own.
        var ctor = containerType.GetConstructor(new[] { serverTypeEnum, typeof(object), typeof(bool) })
            ?? throw new InvalidOperationException("CDataContainer has no (ServerType, object, bool) constructor in this SSMS build.");

        object container = ctor.Invoke(new[] { sqlServerType, connectionInfo, true });

        // SMO URN string literals escape a single quote by doubling it.
        string urn = $"Server[@Name='{EscapeUrn(serverName)}']/JobServer/Job[@Name='{EscapeUrn(jobName)}']";
        var document = new XmlDocument();
        document.LoadXml(BuildFormDescription(serverName, urn, jobId, jobName));

        var documentProperty = containerType.GetProperty("Document")
            ?? throw new InvalidOperationException("CDataContainer.Document is missing in this SSMS build.");
        documentProperty.SetValue(container, document);

        // Neither ServerName nor ObjectName is derived from the document — both read back empty after Document is
        // assigned (verified against a live instance) — so set them directly. With these in place the container
        // reports IsNewObject = false and resolves SqlDialogSubject to the right SMO Job.
        Set(containerType, container, "ServerName", serverName);
        Set(containerType, container, "ObjectName", jobName);

        return container;
    }

    /// <summary>
    /// Mirrors the harvested connection string onto SSMS's own <c>SqlConnectionInfo</c>, so the dialog connects
    /// exactly the way the dashboard already does.
    /// </summary>
    private static object CreateConnectionInfo(SqlConnectionStringBuilder builder, string serverName)
    {
        var assembly = ResolveAssembly(ConnectionInfoAssembly);
        var type = RequireType(assembly, ConnectionInfoType);
        object info = Activator.CreateInstance(type);

        // Data Source is what we connect through; the URN carries the SMO name instead. They differ behind an
        // availability-group listener or a CNAME.
        Set(type, info, "ServerName", NullIfEmpty(builder.DataSource) ?? serverName);
        Set(type, info, "DatabaseName", NullIfEmpty(builder.InitialCatalog) ?? "msdb");
        Set(type, info, "UseIntegratedSecurity", builder.IntegratedSecurity);

        if (!builder.IntegratedSecurity)
        {
            Set(type, info, "UserName", builder.UserID);
            Set(type, info, "Password", builder.Password);
        }

        Set(type, info, "TrustServerCertificate", builder.TrustServerCertificate);
        Set(type, info, "EncryptConnection", IsEncrypted(builder));
        Set(type, info, "ConnectionTimeout", builder.ConnectTimeout);
        Set(type, info, "ApplicationName", "SQLExtended Agent Jobs");

        return info;
    }

    /// <summary>
    /// Whether the connection string asks for encryption. Microsoft.Data.SqlClient 5+ made
    /// <c>Encrypt</c> a tri-state (Optional / Mandatory / Strict) rather than a bool, and its default flipped to
    /// Mandatory — so this cannot just be cast.
    /// </summary>
    private static bool IsEncrypted(SqlConnectionStringBuilder builder) =>
        !string.Equals(builder.Encrypt?.ToString(), "Optional", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(builder.Encrypt?.ToString(), "False", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sets one property if this SSMS build has it. Tolerant by design: the connection-info surface has gained
    /// members over time (StrictEncryption, HostNameInCertificate), and a missing optional setting should not
    /// cost the whole dialog.
    /// </summary>
    private static void Set(Type type, object target, string propertyName, object value)
    {
        var property = type.GetProperty(propertyName);
        if (property == null || !property.CanWrite) return;

        try { property.SetValue(target, value); } catch { }
    }

    /// <summary>
    /// The params document SSMS's management dialogs read. The shape is not a guess: SqlMgmt.dll carries this
    /// template as a string literal, and the dialogs read their inputs with absolute XPaths rooted at
    /// <c>formdescription</c> — <c>/formdescription/params/urn</c>, <c>/formdescription/params/servername</c> and
    /// friends — so a document rooted anywhere else binds nothing and the sheet opens empty.
    ///
    /// <para><b>jobid and job are what put the dialog into edit mode</b>, and the URN alone does not. This is the
    /// non-obvious part of the whole launcher: <c>JobData</c>'s constructor decides its mode from those two
    /// params only —
    /// <code>
    ///   if (originalName.Length > 0 || !string.IsNullOrEmpty(jobIdString)) mode = DialogMode.Properties;
    ///   else { mode = DialogMode.Create; SetDefaults(); }
    /// </code>
    /// — and every panel loader (<c>CheckAndLoadGeneralData</c>, <c>CheckAndLoadOwner</c>, steps, schedules,
    /// notifications) returns immediately when the mode is Create. Omit them and the dialog opens, connects,
    /// resolves nothing and shows a blank New Job sheet, which is exactly what it looks like when it is broken.
    /// The <c>urn</c> is still supplied because <c>JobData.Job</c> falls back to <c>GetSmoObject(urn)</c> when
    /// there is no id, and the scripting path reads it.</para>
    ///
    /// <para>The id is preferred over the name for the same reason <see cref="JobActionService"/> addresses jobs
    /// by <c>@job_id</c>: <c>JobData.Job</c> resolves it with <c>Jobs.ItemById</c> and back-fills both the name
    /// and the URN from the job it finds, so a job renamed since the last poll still opens.</para>
    ///
    /// assemblyname/formtype are what Object Explorer uses to decide which form to launch. We construct the form
    /// directly so they are informational here, but they are included to keep the document the shape SSMS's own
    /// code produces.
    /// </summary>
    private static string BuildFormDescription(string serverName, string urn, Guid jobId, string jobName) =>
        "<formdescription><params>"
        + "<servername>" + Xml(serverName) + "</servername>"
        + "<servertype>sql</servertype>"
        + "<urn>" + Xml(urn) + "</urn>"
        + "<job>" + Xml(jobName) + "</job>"
        + (jobId == Guid.Empty ? "" : "<jobid>" + Xml(jobId.ToString()) + "</jobid>")
        + "<assemblyname>SqlManagerUi.dll</assemblyname>"
        + "<formtype>" + JobSheetType + "</formtype>"
        + "</params></formdescription>";

    private static string Xml(string value) => System.Security.SecurityElement.Escape(value) ?? "";

    private static string EscapeUrn(string value) => value.Replace("'", "''");

    private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Prefers an assembly the SSMS process has already loaded. Loading a second copy from disk would give the
    /// interfaces a different identity, and the ISqlControlCollection check above would then fail on a sheet
    /// that is in fact perfectly valid.
    /// </summary>
    private static Assembly ResolveAssembly(string simpleName)
    {
        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            if (string.Equals(loaded.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                return loaded;

        try { return Assembly.Load(simpleName); } catch { }

        // Not loaded yet and not resolvable by name — fall back to the SSMS install folder, which is the
        // AppDomain base directory when running inside Ssms.exe.
        var probed = new List<string>();
        string ideDir = AppDomain.CurrentDomain.BaseDirectory ?? "";
        foreach (string candidate in new[] { Path.Combine(ideDir, simpleName + ".dll") })
        {
            probed.Add(candidate);
            if (File.Exists(candidate))
                return Assembly.LoadFrom(candidate);
        }

        throw new FileNotFoundException($"SSMS assembly {simpleName}.dll not found. Probed: {string.Join("; ", probed)}");
    }

    private static Type RequireType(Assembly assembly, string fullName) =>
        assembly.GetType(fullName, throwOnError: false)
        ?? throw new InvalidOperationException($"{fullName} not found in {assembly.GetName().Name} (this SSMS version may have moved it).");

    /// <summary>Adapts the shell's owner HWND for <see cref="Form.ShowDialog(IWin32Window)"/>.</summary>
    private sealed class ShellOwner : IWin32Window
    {
        public ShellOwner(IntPtr handle) => Handle = handle;
        public IntPtr Handle { get; }
    }

    /// <summary>
    /// Serves SSMS's <c>LaunchFormHost</c> to whoever asks for one of the interfaces it implements, and passes
    /// every other request through to the package.
    ///
    /// This exists because <c>LaunchForm.InitializeForm</c> resolves its host with
    /// <c>provider.GetService(typeof(ILaunchFormHost))</c> rather than casting the provider — so handing it an
    /// object that merely *is* an ILaunchFormHost fails with the very message that says it must implement one.
    /// Matching on <see cref="Type.IsInstanceOfType"/> covers ILaunchFormHost through ILaunchFormHost5 without
    /// this code having to name any of them.
    /// </summary>
    private sealed class HostServiceProvider : IServiceProvider
    {
        private readonly object _host;
        private readonly IServiceProvider _inner;

        public HostServiceProvider(object host, IServiceProvider inner)
        {
            _host = host;
            _inner = inner;
        }

        public object GetService(Type serviceType)
        {
            if (serviceType != null && _host != null && serviceType.IsInstanceOfType(_host))
                return _host;

            return _inner?.GetService(serviceType);
        }
    }
}
