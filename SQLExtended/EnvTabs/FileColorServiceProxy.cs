using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace SQLExtended.EnvTabs;

/// <summary>
/// Reaches the shell's file-colour service, which is what actually tints a document tab.
///
/// <b>This is the part that differs from how EnvTabs does it, and the difference is worth defending.</b>
/// The shell derives a colour group from a regex line and then picks the colour as
/// <c>Math.Abs(HashHelpers.GetStableHashCode(pattern) % 16)</c> — the hash of the pattern <i>text</i>. The
/// published technique forces a chosen colour by appending a regex comment such as <c>(?#salt:9)</c>,
/// which changes the hash without changing what the pattern matches, and brute-forcing the salt until the
/// hash lands on the wanted index. That works, but it requires reimplementing <c>GetStableHashCode</c>
/// byte-for-byte: it is an internal helper in an assembly we cannot reference, and if our copy ever
/// disagrees with the shell's, every colour silently becomes a different colour — the one failure mode
/// nobody would read as a bug.
///
/// The shell offers a supported way to say the same thing. <c>FileColorService.SetFileColorAsync</c> — the
/// implementation behind the tab's own "Set Tab Color" command — is <c>public</c>, and it records an
/// explicit <c>groupId → colorIndex</c> entry that takes precedence over the hash. So we let the shell
/// compute its own group id from our pattern and simply tell it which colour that group should be. No hash
/// to reproduce, and nothing to re-verify when Microsoft changes it.
///
/// Everything is reflected because the service lives in <c>Microsoft.VisualStudio.Shell.UI.Internal.dll</c>,
/// which ships inside SSMS and is in no SDK package. Failures are recorded and swallowed.
/// </summary>
internal static class FileColorServiceProxy
{
    /// <summary>
    /// The shell's regex file-group provider. Copied from <c>FileColorPackage.RegexFileGroupProviderGuid</c>;
    /// tab colouring only consults our config file while this is the selected provider.
    /// </summary>
    public static readonly Guid RegexFileGroupProviderGuid = new("F282EA13-0551-44CF-8646-B8083627AC40");

    private static Type _serviceContract;
    private static object _service;
    private static bool _serviceProbed;

    /// <summary>
    /// Pins <paramref name="colorIndex"/> onto whichever colour group <paramref name="filePath"/> currently
    /// falls into. The config file must already name the path, or the shell resolves no group and the call
    /// is a no-op — so callers write the config first.
    ///
    /// <paramref name="hierarchy"/> may be anything non-null: the regex provider ignores it entirely and
    /// keys only off the path, but the service argument-checks it before getting that far.
    /// </summary>
    public static async Task<bool> SetFileColorAsync(IServiceProvider serviceProvider, string filePath, IVsHierarchy hierarchy, uint itemId, int colorIndex, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(filePath) || hierarchy == null) return false;
        if (!EnvTabPalette.IsValid(colorIndex)) return false;

        try
        {
            object service = GetService(serviceProvider);
            if (service == null) return false;

            MethodInfo method = _serviceContract.GetMethod("SetFileColorAsync");
            if (method == null)
            {
                EnvTabsDiagnostics.Note("The shell's file-colour service has no SetFileColorAsync — tab colours cannot be pinned.");
                return false;
            }

            if (method.Invoke(service, new object[] { filePath, hierarchy, itemId, colorIndex, token }) is Task task)
                await task.ConfigureAwait(false);

            return true;
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not set a tab colour: " + Unwrap(ex).Message);
            return false;
        }
    }

    private static object GetService(IServiceProvider serviceProvider)
    {
        if (_serviceProbed) return _service;
        _serviceProbed = true;

        try
        {
            Type serviceKey = FindShellType("Microsoft.VisualStudio.PlatformUI.Packages.FileColor.SFileColorService");
            _serviceContract = FindShellType("Microsoft.VisualStudio.PlatformUI.Packages.FileColor.IFileColorService");

            if (serviceKey == null || _serviceContract == null)
            {
                EnvTabsDiagnostics.Note("The shell's file-colour service is not present in this SSMS build — captions will still be applied, colours will not.");
                return null;
            }

            _service = serviceProvider?.GetService(serviceKey);
            if (_service == null)
                EnvTabsDiagnostics.Note("The shell's file-colour service did not resolve — tab colours will not be pinned.");

            return _service;
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not reach the shell's file-colour service: " + Unwrap(ex).Message);
            return null;
        }
    }

    /// <summary>
    /// Turns on "colorize document tabs" and selects the regex provider, which is what makes the config
    /// file take effect at all.
    ///
    /// Without this the feature is invisible and looks broken — the file is written correctly and the
    /// shell simply never consults it. It is set once at startup rather than every poll because it is a
    /// user-visible preference: someone who turns tab colouring off in Tools &gt; Options has said
    /// something, and a poll that kept re-enabling it would be fighting them.
    /// </summary>
    public static bool EnableRegexTabColoring(bool enable)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            Type viewManagerType = FindShellType("Microsoft.VisualStudio.PlatformUI.Shell.ViewManager");
            object instance = viewManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object preferences = instance?.GetType().GetProperty("Preferences")?.GetValue(instance);

            if (preferences == null)
            {
                EnvTabsDiagnostics.Note("Could not reach the shell's tab-colour preferences; turn on Tools > Options > Environment > Tabs and Windows > Colorize document tabs (by regex) by hand.");
                return false;
            }

            PropertyInfo colorize = preferences.GetType().GetProperty("ColorizeDocumentTabs");
            PropertyInfo provider = preferences.GetType().GetProperty("CurrentFileGroupProvider");

            if (colorize == null || provider == null)
            {
                EnvTabsDiagnostics.Note("The shell's tab-colour preferences have changed shape; set them by hand in Tools > Options.");
                return false;
            }

            if (enable)
            {
                provider.SetValue(preferences, RegexFileGroupProviderGuid);
                colorize.SetValue(preferences, true);
            }
            else
            {
                colorize.SetValue(preferences, false);
            }

            return true;
        }
        catch (Exception ex)
        {
            EnvTabsDiagnostics.Note("Could not change the shell's tab-colour preference: " + Unwrap(ex).Message);
            return false;
        }
    }

    /// <summary>True when the shell is currently set to colour tabs from the regex provider.</summary>
    public static bool IsRegexTabColoringOn()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            Type viewManagerType = FindShellType("Microsoft.VisualStudio.PlatformUI.Shell.ViewManager");
            object instance = viewManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            object preferences = instance?.GetType().GetProperty("Preferences")?.GetValue(instance);
            if (preferences == null) return false;

            bool on = preferences.GetType().GetProperty("ColorizeDocumentTabs")?.GetValue(preferences) is true;
            object current = preferences.GetType().GetProperty("CurrentFileGroupProvider")?.GetValue(preferences);
            return on && current is Guid guid && guid == RegexFileGroupProviderGuid;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Finds a type by full name among the assemblies already loaded in the process. Loading it from disk
    /// instead would give a second copy with a different identity, and the service returned by
    /// <c>GetService</c> would then fail to match the contract — the same trap documented for
    /// <c>JobDialogLauncher</c>.
    /// </summary>
    private static Type FindShellType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type found = assembly.GetType(fullName, throwOnError: false);
                if (found != null) return found;
            }
            catch
            {
                // An assembly that won't answer is not the one we want.
            }
        }
        return null;
    }

    /// <summary>Reflection failures arrive wrapped; the inner message is the only informative one.</summary>
    private static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
}
