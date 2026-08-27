using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended.EnvTabs;

/// <summary>
/// Finds and rewrites the shell's <c>ColorByRegexConfig.txt</c>.
///
/// <b>The path is asked for, never constructed.</b> The shell resolves it through
/// <c>IVsSolutionWorkingFolders.GetFolder</c> and appends the file name; with no solution open — the
/// normal state in SSMS — that folder is a per-session location that has moved between releases and is
/// documented nowhere. Guessing it is how this feature would silently write a file nobody reads, so we
/// call the same API with the same arguments the shell's own <c>RegexFileProvider.DetermineFilePathAsync</c>
/// passes and use whatever comes back.
///
/// <b>Why reflection.</b> <c>IVsSolutionWorkingFolders</c> lives in
/// <c>Microsoft.Internal.VisualStudio.Shell.Interop</c> and ships only inside VS/SSMS — it is in none of
/// the public VS SDK packages this project references, so there is nothing to compile against. This is the
/// same bargain <see cref="ConnectionHelper"/> makes, and it is wrapped the same way: every failure path
/// returns null rather than throwing into a tab update.
///
/// The shell watches this file with <c>IVsFileChangeEx</c> and reloads on change, so writing it is the
/// whole mechanism — there is nothing to notify afterwards.
/// </summary>
internal static class ColorByRegexConfigStore
{
    public const string FileName = "ColorByRegexConfig.txt";

    /// <summary>
    /// The folder id the shell passes. Left as a literal on purpose: it is copied from
    /// <c>RegexFileProvider.DetermineFilePathAsync</c>, and matching the shell matters more than naming
    /// the constant after a guess at which enum member it is.
    /// </summary>
    private const uint ShellWorkingFolderId = 1u;

    private static string _cachedPath;
    private static bool _pathResolved;

    /// <summary>
    /// Resolves the full path, or null if the shell won't tell us. Must be called on the UI thread —
    /// the shell switches to the main thread before calling this API itself.
    ///
    /// The answer is memoised, including a failure: this runs on a poll, and neither a working folder nor
    /// a missing interop assembly changes within a session.
    /// </summary>
    public static string ResolvePath(IServiceProvider serviceProvider)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_pathResolved) return _cachedPath;

        _pathResolved = true;
        _cachedPath = TryResolvePath(serviceProvider);
        return _cachedPath;
    }

    private static string TryResolvePath(IServiceProvider serviceProvider)
    {
        object solution = null;
        IntPtr unknown = IntPtr.Zero;
        try
        {
            solution = serviceProvider?.GetService(typeof(SVsSolution));
            if (solution == null) return null;

            Type contract = FindWorkingFoldersInterface();
            if (contract == null) return null;

            MethodInfo getFolder = contract.GetMethod("GetFolder");
            if (getFolder == null) return null;

            // The service is a COM object; a typed RCW is needed before the interface method is callable
            // (these interfaces are not IDispatch, so InvokeMember on the __ComObject would fail).
            unknown = Marshal.GetIUnknownForObject(solution);
            object typed = Marshal.GetTypedObjectForIUnknown(unknown, contract);
            if (typed == null) return null;

            // (folderId, provider, fVersionSpecific, fEnsureCreated, ref fIsTemporary, ref pathOut)
            object[] args = { ShellWorkingFolderId, Guid.Empty, false, true, false, null };
            getFolder.Invoke(typed, args);

            string folder = args[5] as string;
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, FileName);
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not resolve the tab-colour config path: " + ex.Message);
            return null;
        }
        finally
        {
            if (unknown != IntPtr.Zero) Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// Locates the interop interface among the assemblies SSMS has already loaded. Matched on type name
    /// alone rather than an assembly-qualified name, because which interop assembly carries it has moved
    /// between shell versions and the name has not.
    /// </summary>
    private static Type FindWorkingFoldersInterface()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type found = null;
            try
            {
                found = assembly.GetTypes().FirstOrDefault(t => t.IsInterface && t.Name == "IVsSolutionWorkingFolders");
            }
            catch (ReflectionTypeLoadException ex)
            {
                found = ex.Types?.FirstOrDefault(t => t != null && t.IsInterface && t.Name == "IVsSolutionWorkingFolders");
            }
            catch
            {
                // A assembly that won't enumerate is not the one we want.
            }

            if (found != null) return found;
        }
        return null;
    }

    /// <summary>
    /// Rewrites the managed block at <paramref name="path"/>, preserving everything else in the file.
    /// Returns true when the file was changed; false when it already said the same thing, which is the
    /// common case on a poll and is worth skipping — every write wakes the shell's file watcher and makes
    /// it recompile every pattern.
    /// </summary>
    public static bool Write(string path, IEnumerable<EnvTabGroup> groups)
    {
        if (string.IsNullOrEmpty(path)) return false;

        try
        {
            string existing = File.Exists(path) ? File.ReadAllText(path) : "";
            string updated = ColorByRegexConfigText.Merge(existing, groups);

            if (string.Equals(existing, updated, StringComparison.Ordinal)) return false;

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, updated);
            return true;
        }
        catch (Exception ex)
        {
            // The shell holds this file open to watch it and reads it on its own schedule. A clash here
            // costs one refresh, not the feature — the next poll writes the same content again.
            EnvTabsDiagnostics.Note("Could not write the tab-colour config: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Removes our block and leaves the rest of the file intact. Called when the feature is switched off,
    /// so turning it off actually removes the colours rather than freezing them at their last values.
    /// </summary>
    public static void RemoveManagedBlock(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            string existing = File.ReadAllText(path);
            string stripped = ColorByRegexConfigText.StripManagedBlock(existing);
            if (!string.Equals(existing, stripped, StringComparison.Ordinal))
                File.WriteAllText(path, stripped.Replace("\n", "\r\n"));
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not clear the tab-colour config: " + ex.Message);
        }
    }
}
