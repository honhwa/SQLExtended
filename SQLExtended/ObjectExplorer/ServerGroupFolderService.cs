using Microsoft.SqlServer.Management.UI.VSIntegration.ObjectExplorer;
using Microsoft.VisualStudio.Shell;
using SQLExtended.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Task = System.Threading.Tasks.Task;

namespace SQLExtended.ObjectExplorer;

/// <summary>
/// Groups connected servers in the Object Explorer tree into folders that mirror the SSMS
/// Registered Servers group hierarchy (see <see cref="RegisteredServersReader"/>).
///
/// Unlike node-level grouping (e.g. Nicholas Ross's SSMS-Schema-Folders, which reorganizes children
/// under a database via the tree's BeforeExpand/AfterExpand events), server nodes are the tree's
/// <b>root</b> nodes and have no expand event to hook. So instead we poll the root Nodes collection
/// on a UI-thread <see cref="System.Windows.Forms.Timer"/> and reparent any misplaced server node
/// into the folder chain for its Registered Servers group, creating folder nodes as needed and
/// pruning empty ones. A cheap signature check skips the work when the tree hasn't changed.
///
/// Folder nodes we create are marked with <see cref="GroupFolderTag"/> so we can tell them apart
/// from real server nodes (here and in <see cref="ObjectExplorerHelper"/>, which walks through them
/// when enumerating servers / revealing objects). All tree access happens on the UI thread and is
/// wrapped in try/catch so a failure never destabilizes the SSMS shell.
/// </summary>
internal static class ServerGroupFolderService
{
    /// <summary>Tag placed on <see cref="TreeNode.Tag"/> of the folder nodes we insert.</summary>
    internal const string GroupFolderTag = "SQLExtended.ServerGroupFolder";

    private static TreeView _tree;
    private static Timer _timer;
    private static bool _busy;
    private static string _lastSignature;
    private static int _folderImageIndex = -1;

    /// <summary>
    /// Resolves the Object Explorer tree (polling briefly, as it may not exist at package load) and
    /// starts the grouping timer. No-op thereafter if the tree is never found.
    /// </summary>
    public static async Task InitializeAsync(AsyncPackage package)
    {
        for (int attempt = 0; attempt < 20 && _tree == null; attempt++)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (ObjectExplorerHelper.GetObjectExplorerTree() is TreeView tree)
            {
                _tree = tree;
                ResolveFolderImageIndex(tree);

                int seconds = Math.Max(1, SQLExtendedSettings.Current.ServerGroupPollSeconds);
                _timer = new Timer { Interval = seconds * 1000 };
                _timer.Tick += (s, e) => Tick();
                _timer.Start();
                return;
            }

            await Task.Delay(1000);
        }
    }

    public static void Dispose()
    {
        try
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }
        }
        catch { }
        _timer = null;
        _tree = null;
        _busy = false;
        _lastSignature = null;
    }

    private static void Tick()
    {
        if (_busy) return;
        if (!SQLExtendedSettings.Current.ServerGroupingEnabled) return;

        _busy = true;
        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var tree = _tree;
            if (tree == null || tree.Nodes.Count == 0)
                return;

            // Skip the (reflection + tree walk) work when nothing about the top of the tree changed.
            string sig = ComputeSignature(tree);
            if (sig == _lastSignature)
                return;

            var map = RegisteredServersReader.BuildServerGroupMap();
            if (map.Count == 0)
            {
                // No registered groups (or store unreadable): leave the tree as-is, but remember the
                // signature so we don't re-read the store every tick until the tree changes again.
                _lastSignature = sig;
                return;
            }

            Regroup(tree, map);
            _lastSignature = ComputeSignature(tree);
        }
        catch
        {
            // Never let a grouping failure crash the shell.
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Forces an immediate grouping pass (ignoring the change-detection signature and the poll
    /// interval). Returns a human-readable per-server outcome report, or null if the Object Explorer
    /// tree isn't available yet. Must be called on the UI thread.
    /// </summary>
    public static List<string> RegroupNow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var tree = _tree;
        if (tree == null || tree.Nodes.Count == 0)
            return null;

        var map = RegisteredServersReader.BuildServerGroupMap();
        var report = Regroup(tree, map);
        _lastSignature = ComputeSignature(tree);
        return report;
    }

    /// <summary>
    /// Moves every server node to the folder chain its Registered Servers group demands, creating
    /// missing folders and removing any that end up empty. Idempotent: nodes already in the right
    /// place are left untouched. Returns the per-server outcome report (also written to the log).
    /// </summary>
    private static List<string> Regroup(TreeView tree, Dictionary<string, List<string>> map)
    {
        var report = new List<string>();

        tree.BeginUpdate();
        try
        {
            var servers = new List<TreeNode>();
            CollectServerNodes(tree.Nodes, servers);

            foreach (var node in servers)
            {
                string label = "[" + string.Join(", ", CandidateNames(node)) + "]"
                             + (node.IsExpanded ? " (expanded)" : "");
                try
                {
                    // A server that matches no registered entry is left exactly where it is (don't yank
                    // an already-grouped server back to the root just because the store momentarily can't
                    // be matched). null = no match; an empty list = matched but registered at the root.
                    List<string> target = ResolveTargetPath(node, map);
                    if (target == null)
                    {
                        report.Add("  NO MATCH, left in place: " + label);
                        continue;
                    }

                    if (SamePath(FolderPathOf(node), target))
                    {
                        report.Add("  already placed: " + label);
                        continue;
                    }

                    bool wasExpanded = node.IsExpanded;
                    node.Remove();
                    if (target.Count == 0)
                        tree.Nodes.Add(node);
                    else
                        EnsureFolderPath(tree, target).Nodes.Add(node);
                    if (wasExpanded)
                        node.Expand();

                    report.Add("  MOVED to '" + (target.Count == 0 ? "(root)" : string.Join(" / ", target)) + "': " + label);
                }
                catch (Exception ex)
                {
                    // Isolate per-node failures so one bad move can't block grouping the rest.
                    report.Add("  EXCEPTION moving " + label + ": " + ex.Message);
                    System.Diagnostics.Debug.WriteLine($"[SQLExtended] Regroup node '{node?.Text}' failed: {ex}");
                }
            }

            RemoveEmptyFolders(tree.Nodes);
        }
        finally
        {
            tree.EndUpdate();
        }

        WriteDiagnostics(report, map);
        return report;
    }

    /// <summary>
    /// Resolves the group folder path for a server node, trying every candidate name form the node
    /// exposes (display text and Object Explorer node info) against the (normalized) group map.
    /// Returns the matched path (possibly empty for a root-registered server), or null if no
    /// registered server matches any candidate.
    /// </summary>
    private static List<string> ResolveTargetPath(TreeNode node, Dictionary<string, List<string>> map)
    {
        foreach (string candidate in CandidateNames(node))
            if (map.TryGetValue(RegisteredServersReader.Normalize(candidate), out var path))
                return path;
        return null;
    }

    /// <summary>
    /// The server-name forms a node might match on: its display text (before the " (...)" suffix) and,
    /// when available, the names carried by its Object Explorer <see cref="INodeInformation"/>
    /// (its Name and the Server[@Name='...'] in its NavigationContext). More reliable than text alone.
    /// </summary>
    private static IEnumerable<string> CandidateNames(TreeNode node)
    {
        var names = new List<string>();
        if (node == null) return names;

        string text = node.Text;
        if (!string.IsNullOrEmpty(text))
        {
            int paren = text.IndexOf('(');
            names.Add((paren > 0 ? text.Substring(0, paren) : text).Trim());
        }

        try
        {
            if (node is IServiceProvider sp && sp.GetService(typeof(INodeInformation)) is INodeInformation ni)
            {
                if (!string.IsNullOrEmpty(ni.Name))
                    names.Add(ni.Name.Trim());

                string ctx = ni.NavigationContext ?? "";
                const string marker = "Server[@Name='";
                int i = ctx.IndexOf(marker, StringComparison.Ordinal);
                if (i >= 0)
                {
                    int start = i + marker.Length;
                    int end = ctx.IndexOf('\'', start);
                    if (end > start)
                        names.Add(ctx.Substring(start, end - start).Trim());
                }
            }
        }
        catch { }

        return names.Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Overwrites a small log at %APPDATA%\SQLExtended\SSMS\server-grouping.log with the outcome of the
    /// last grouping pass (per-server: moved / already placed / no match / exception) and the available
    /// Registered Servers group keys — so a name mismatch (alias/IP vs actual server name) or a move
    /// failure can be diagnosed from the field.
    /// </summary>
    private static void WriteDiagnostics(List<string> report, Dictionary<string, List<string>> map)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SQLExtended", "SSMS");
            Directory.CreateDirectory(dir);

            var lines = new List<string> { "SQLExtended server grouping — last pass outcome", "" };
            lines.AddRange(report.Count > 0 ? report : new List<string> { "  (no server nodes found)" });

            lines.Add("");
            lines.Add("Registered Servers group keys (normalized name -> folder path):");
            foreach (var kv in map.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add("  " + kv.Key + "  ->  " + (kv.Value.Count == 0 ? "(root)" : string.Join(" / ", kv.Value)));

            File.WriteAllText(Path.Combine(dir, "server-grouping.log"), string.Join(Environment.NewLine, lines));
        }
        catch { }
    }

    /// <summary>
    /// Collects the real (non-folder) nodes reachable at the root or inside our group folders. We
    /// only ever descend through our own folders, so a server node's own children (Databases, etc.)
    /// are never mistaken for servers.
    /// </summary>
    private static void CollectServerNodes(TreeNodeCollection nodes, List<TreeNode> outList)
    {
        foreach (TreeNode n in nodes)
        {
            if (IsGroupFolder(n))
                CollectServerNodes(n.Nodes, outList);
            else
                outList.Add(n);
        }
    }

    /// <summary>Navigates to (creating as needed) the nested folder chain and returns the deepest folder.</summary>
    private static TreeNode EnsureFolderPath(TreeView tree, List<string> path)
    {
        TreeNodeCollection level = tree.Nodes;
        TreeNode folder = null;

        foreach (string name in path)
        {
            folder = FindFolder(level, name);
            if (folder == null)
            {
                folder = new TreeNode(name) { Tag = GroupFolderTag, Name = "SQLExtendedGroup::" + name };
                if (_folderImageIndex >= 0)
                {
                    folder.ImageIndex = _folderImageIndex;
                    folder.SelectedImageIndex = _folderImageIndex;
                }
                level.Add(folder);
            }
            level = folder.Nodes;
        }

        return folder;
    }

    private static TreeNode FindFolder(TreeNodeCollection nodes, string name)
    {
        foreach (TreeNode n in nodes)
            if (IsGroupFolder(n) && string.Equals(n.Text, name, StringComparison.OrdinalIgnoreCase))
                return n;
        return null;
    }

    /// <summary>Removes our (and only our) folder nodes that have no remaining children. Depth-first.</summary>
    private static void RemoveEmptyFolders(TreeNodeCollection nodes)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            TreeNode n = nodes[i];
            if (!IsGroupFolder(n))
                continue;

            RemoveEmptyFolders(n.Nodes);
            if (n.Nodes.Count == 0)
                n.Remove();
        }
    }

    /// <summary>The chain of group-folder names above <paramref name="node"/> (outermost first).</summary>
    private static List<string> FolderPathOf(TreeNode node)
    {
        var path = new List<string>();
        for (TreeNode parent = node.Parent; parent != null && IsGroupFolder(parent); parent = parent.Parent)
            path.Add(parent.Text);
        path.Reverse();
        return path;
    }

    private static bool SamePath(List<string> a, List<string> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!string.Equals(a[i], b[i], StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    private static bool IsGroupFolder(TreeNode node) =>
        node != null && GroupFolderTag.Equals(node.Tag as string);

    /// <summary>
    /// Signature of the tree's grouping-relevant shape: every real server node's location. Changes
    /// when a server connects/disconnects or moves, so an unchanged signature means no work to do.
    /// </summary>
    private static string ComputeSignature(TreeView tree)
    {
        var servers = new List<TreeNode>();
        CollectServerNodes(tree.Nodes, servers);

        return string.Join("|", servers
            .Select(n => string.Join("/", FolderPathOf(n)) + ">" + (n.Text ?? ""))
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Best-effort: find a folder-like image in the OE tree's ImageList to give group folders a
    /// recognizable icon. Falls back to no custom image (leaves the default) when none is found.
    /// </summary>
    private static void ResolveFolderImageIndex(TreeView tree)
    {
        try
        {
            var images = tree.ImageList?.Images;
            if (images == null) return;

            for (int i = 0; i < images.Count; i++)
            {
                string key = images.Keys[i];
                if (!string.IsNullOrEmpty(key) && key.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _folderImageIndex = i;
                    return;
                }
            }
        }
        catch { }
    }
}
