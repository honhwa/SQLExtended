using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.Shell;

namespace SQLExtended.Monitoring;

/// <summary>
/// Shared plumbing for the four dashboards' "pinned to one server, one window per server" behaviour.
///
/// All four monitors are pinned to the connection they were opened from rather than following the active query
/// window, and all four are registered <c>MultiInstances</c> so one window per server can be open at once. The
/// reasoning is the same in every case: these windows are left up for minutes or hours while you work in query
/// windows connected elsewhere, and a dashboard that silently re-pointed itself would move every reading — and,
/// for the two dashboards that can act on the server, every action — onto whichever editor last had focus.
///
/// It lives here beside <see cref="RowMerge"/> and <c>MonitoringTheme.xaml</c> for the reason those do: four
/// copies of this would drift, and the difference between them would be felt as one dashboard behaving unlike
/// the others rather than as a bug anyone goes looking for.
/// </summary>
internal interface IPinnedMonitorPane
{
    /// <summary>
    /// Instance id the pane was created under, assigned by <see cref="MonitorWindows.AcquireAsync"/>. Kept so the
    /// id can be freed when the window closes: multi-instance panes are destroyed rather than hidden, and a leaked
    /// id would burn one of the slots for the rest of the session.
    /// </summary>
    int InstanceId { get; set; }

    /// <summary>The server this window is pinned to, or null while unpinned. Comes from the hosted control.</summary>
    string PinnedServerKey { get; }
}

/// <summary>
/// Owns dashboard tool window instances: which server each is pinned to, which instance ids are in use, and the
/// rules for choosing a window when a server is asked for.
/// </summary>
internal static class MonitorWindows
{
    /// <summary>
    /// How many windows of one dashboard can be open at once. Each polls on its own timer, so this caps background
    /// load as much as screen clutter; ten is already past what fits usefully in a docked tab well.
    /// </summary>
    public const int MaxWindows = 10;

    // Per pane type, the live windows by instance id. Panes unregister themselves from Dispose.
    private static readonly Dictionary<Type, Dictionary<int, IPinnedMonitorPane>> _open = new Dictionary<Type, Dictionary<int, IPinnedMonitorPane>>();

    /// <summary>The instance a connection string points at, as typed — <c>HOST\INSTANCE</c>, a listener, a CNAME.</summary>
    public static string ServerTarget(string connectionString)
    {
        try { return new SqlConnectionStringBuilder(connectionString).DataSource; } catch { return null; }
    }

    /// <summary>
    /// The login a connection string authenticates as, for the header's "as &lt;login&gt;". SQL auth spells it out;
    /// integrated security does not, so the process's own Windows identity stands in — that is precisely what the
    /// server will report as the login. Null when neither is expressible (an access token, say), which the header
    /// then simply leaves blank rather than guessing at.
    ///
    /// This is the value shown until the first poll comes back with the server's own answer, for the same reason
    /// <see cref="MonitorPin.Target"/> stands in for the server name: it is available immediately and when the
    /// server cannot be reached at all.
    /// </summary>
    public static string ConnectionLogin(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(builder.UserID)) return builder.UserID.Trim();

            return builder.IntegratedSecurity ? NullIfEmpty(System.Security.Principal.WindowsIdentity.GetCurrent()?.Name) : null;
        }
        catch { return null; }
    }

    private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The identity a window is pinned by, so opening a dashboard twice for one server reuses the window instead
    /// of stacking up duplicates that then poll the same instance in parallel.
    ///
    /// Deliberately the connect target alone and not the login: "one window per server" is the mental model, and
    /// keying on credentials too would silently open a second window whenever the same box was reached from a query
    /// window authenticating differently.
    /// </summary>
    public static string ServerKey(string connectionString)
    {
        string target = ServerTarget(connectionString);
        return string.IsNullOrWhiteSpace(target) ? null : target.Trim().ToUpperInvariant();
    }

    /// <summary>Called by a pane as it is disposed, so its instance id becomes available again.</summary>
    public static void Forget(IPinnedMonitorPane pane)
    {
        if (pane == null) return;

        foreach (var byId in _open.Values)
        {
            // By value, not by InstanceId: the id is assigned after creation, and a pane that failed before that
            // point would otherwise sit in the dictionary forever holding a slot.
            foreach (var entry in byId.Where(e => ReferenceEquals(e.Value, pane)).ToList())
                byId.Remove(entry.Key);
        }
    }

    /// <summary>
    /// Picks the window to use for a server and shows it. In order: the window already pinned to that server, else
    /// a new instance on the lowest free id, else — at <see cref="MaxWindows"/> — the lowest-numbered existing
    /// window, which the caller then re-pins. <c>AtCap</c> says whether that last case applied, because displacing
    /// a window's contents is something the user has to be told about rather than left to notice.
    /// </summary>
    /// <param name="serverKey">
    /// From <see cref="ServerKey"/>, or null when no connection could be resolved. Null reuses the lowest-numbered
    /// open window rather than opening another one that cannot connect either.
    /// </param>
    public static async System.Threading.Tasks.Task<(IPinnedMonitorPane Pane, bool AtCap)> AcquireAsync(AsyncPackage package, Type paneType, string serverKey)
    {
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (!_open.TryGetValue(paneType, out var windows))
            _open[paneType] = windows = new Dictionary<int, IPinnedMonitorPane>();

        var existing = windows.OrderBy(e => e.Key)
                              .Select(e => e.Value)
                              .FirstOrDefault(w => string.Equals(w.PinnedServerKey, serverKey, StringComparison.Ordinal));
        if (existing != null) return (existing, false);

        if (serverKey == null && windows.Count > 0)
            return (windows.OrderBy(e => e.Key).First().Value, false);

        int id = FirstFreeId(windows);
        bool atCap = id < 0;
        if (atCap) id = windows.Keys.OrderBy(k => k).First();

        var pane = await package.ShowToolWindowAsync(paneType, id, create: true, package.DisposalToken) as IPinnedMonitorPane;
        await package.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

        if (pane != null)
        {
            pane.InstanceId = id;
            windows[id] = pane;
        }

        return (pane, atCap);
    }

    private static int FirstFreeId(Dictionary<int, IPinnedMonitorPane> windows)
    {
        for (int id = 0; id < MaxWindows; id++)
            if (!windows.ContainsKey(id)) return id;

        return -1;
    }
}

/// <summary>
/// One dashboard's pin: the connection it was opened with, and the names to show for it.
///
/// The connection is kept exactly as it was harvested, not normalised to a database. Each dashboard re-points it
/// at whatever it needs on every poll (msdb, master, the distribution database, a subscriber database), and the
/// replication monitor needs three at once — so normalising here would throw away what it has to derive from.
/// </summary>
internal sealed class MonitorPin
{
    /// <summary>The pinned connection as harvested, or null while unpinned.</summary>
    public string Connection { get; private set; }

    /// <summary>The instance this window connects <i>through</i>, i.e. the connection string's Data Source.</summary>
    public string Target { get; private set; }

    /// <summary>
    /// The login the pinned connection authenticates as, as far as the connection string can say — see
    /// <see cref="MonitorWindows.ConnectionLogin"/>. Every reading and every action in these windows is taken as
    /// this login, and with several windows open on servers reached with different rights the header is the only
    /// place that is visible.
    /// </summary>
    public string Login { get; private set; }

    /// <summary>Matching identity — see <see cref="MonitorWindows.ServerKey"/>.</summary>
    public string ServerKey { get; private set; }

    public bool IsPinned => !string.IsNullOrEmpty(Connection);

    /// <summary>
    /// Pins to a connection. Returns true when this is a <i>different</i> instance than the one already pinned,
    /// which is the signal to throw away per-server state: cached capability probes, delta baselines, history
    /// buffers and the grids themselves all describe the old server and none of it transfers.
    /// </summary>
    public bool Set(string connectionString)
    {
        string key = MonitorWindows.ServerKey(connectionString);
        bool changed = !string.Equals(key, ServerKey, StringComparison.Ordinal);

        Connection = connectionString;
        Target = MonitorWindows.ServerTarget(connectionString);
        Login = MonitorWindows.ConnectionLogin(connectionString);
        ServerKey = key;

        return changed;
    }

    /// <summary>
    /// The tooltip for the window's server label. Names the connect target even when it agrees with the server's
    /// own name, because it answers both "which of these windows is which" and "why does this not say what I
    /// typed" — behind an availability-group listener or a CNAME the two differ.
    /// </summary>
    public string Describe(string displayName, string login = null)
    {
        if (Target == null) return null;

        string alias = string.Equals(Target, displayName, StringComparison.OrdinalIgnoreCase) ? "" : $" (reports itself as {displayName})";
        string as_ = string.IsNullOrEmpty(login) ? "" : $"  Connected as {login}.";
        return "Pinned to " + Target + alias + "." + as_ + "  This window keeps this connection regardless of which query window has focus.";
    }

    /// <summary>
    /// What to show as the login: the server's own answer when a poll has produced one, else what the connection
    /// string says. Same preference order as the server name — the server is authoritative (it resolves a Windows
    /// group login, an alias, a contained user), but it is only known once a poll has succeeded.
    /// </summary>
    public string LoginFor(string reportedLogin) => string.IsNullOrWhiteSpace(reportedLogin) ? Login : reportedLogin.Trim();
}
