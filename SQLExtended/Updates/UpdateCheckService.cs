using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Newtonsoft.Json;
using SQLExtended.Diagnostics;
using SQLExtended.Settings;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SQLExtended.Updates;

/// <summary>
/// Polls the configured version.json feed once per session and shows a VS InfoBar when a newer
/// installer is available. Mirrors the Redgate/SSMSBoost UX: manual install, SSMS restart required.
/// </summary>
public sealed class UpdateCheckService
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly TimeSpan CheckCooldown = TimeSpan.FromHours(20);

    private static UpdateCheckService _instance;

    private readonly AsyncPackage _package;

    public UpdateCheckService(AsyncPackage package)
    {
        _package = package;
        _instance = this;
    }

    /// <summary>Fire-and-forget entry point. Safe to call from package init — never throws.</summary>
    public void StartBackgroundCheck()
    {
        _ = Task.Run(async () =>
        {
            try { await RunAsync(_package.DisposalToken).ConfigureAwait(false); }
            // Soft by design — a feed that is unreachable, malformed or blocked by a proxy must not disturb
            // SSMS. Routed to the session log as well as Debug.WriteLine, which is [Conditional("DEBUG")]
            // and so reports nothing at all from the Release build this actually ships as.
            catch (Exception ex)
            {
                SQLExtendedLog.Error(nameof(UpdateCheckService), "Update check failed", ex);
                Debug.WriteLine($"[SQLExtended] UpdateCheck failed: {ex}");
            }
        });
    }

    /// <summary>Called by the "Check Now" UI button. Bypasses the cooldown — caller is expected to have already cleared UpdateLastCheckUtc.</summary>
    public static void RunManualCheck()
    {
        _instance?.StartBackgroundCheck();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var settings = SQLExtendedSettings.Load();
        if (!settings.UpdateCheckEnabled || string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))
            return;

        if (DateTime.UtcNow - settings.UpdateLastCheckUtc < CheckCooldown)
            return;

        var manifest = await FetchManifestAsync(settings.UpdateFeedUrl, ct).ConfigureAwait(false);
        if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
            return;

        settings.UpdateLastCheckUtc = DateTime.UtcNow;
        settings.Save();

        var current = GetCurrentVersion();
        if (!Version.TryParse(manifest.Version, out var available)) return;
        if (available <= current) return;

        bool isRequired = Version.TryParse(manifest.MinRequiredVersion, out var minRequired) && current < minRequired;

        // Respect "skip this version" unless this is a forced update.
        if (!isRequired &&
            Version.TryParse(settings.UpdateSkippedVersion, out var skipped) &&
            skipped >= available)
        {
            return;
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(ct);
        UpdateInfoBar.Show(_package, current, manifest, isRequired);
    }

    private static async Task<VersionManifest> FetchManifestAsync(string url, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            SQLExtendedLog.Warning(nameof(UpdateCheckService), $"Update feed returned {(int)resp.StatusCode} {resp.ReasonPhrase} for {url}");
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

        // Strip a leading BOM. The feed is a file someone published, and a UTF-8 BOM is what most Windows
        // tooling writes by default (Set-Content -Encoding utf8, Notepad); Json.NET reads the U+FEFF as an
        // unexpected character and throws, which reaches the user as the update check simply never finding
        // anything again. Cheaper to tolerate here than to rely on every future publisher getting it right.
        json = json.TrimStart('\uFEFF');

        return JsonConvert.DeserializeObject<VersionManifest>(json);
    }

    internal static Version GetCurrentVersion()
    {
        var asm = typeof(UpdateCheckService).Assembly;
        var info = FileVersionInfo.GetVersionInfo(asm.Location);
        if (Version.TryParse(info.FileVersion, out var fv)) return fv;
        return asm.GetName().Version ?? new Version(0, 0, 0, 0);
    }

    private static HttpClient CreateHttpClient()
    {
        // SSMS 22 runs on .NET Framework — TLS 1.2 is usually default but pin it to be safe.
        try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"SQLExtended/{GetCurrentVersion()}");
        return client;
    }
}

/// <summary>
/// Renders the update notification as a top-of-window VS InfoBar with Download / Notes / Skip actions.
/// </summary>
internal static class UpdateInfoBar
{
    private const string ActionDownload = "download";
    private const string ActionNotes = "notes";
    private const string ActionSkip = "skip";

    public static void Show(AsyncPackage package, Version current, VersionManifest manifest, bool isRequired)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var sp = (IServiceProvider)package;
        if (!(sp.GetService(typeof(SVsShell)) is IVsShell shell)) return;
        if (!(sp.GetService(typeof(SVsInfoBarUIFactory)) is IVsInfoBarUIFactory factory)) return;

        shell.GetProperty((int)__VSSPROPID7.VSSPROPID_MainWindowInfoBarHost, out object hostObj);
        if (!(hostObj is IVsInfoBarHost host)) return;

        var label = isRequired
            ? $"SQLExtended for SSMS {manifest.Version} is required (you have {current}). Close SSMS before installing the download."
            : $"SQLExtended for SSMS {manifest.Version} is available (you have {current}). Close SSMS before installing the download.";

        var actions = new System.Collections.Generic.List<IVsInfoBarActionItem>
        {
            new InfoBarHyperlink("Download and install", ActionDownload),
        };
        if (!string.IsNullOrWhiteSpace(manifest.Notes))
            actions.Add(new InfoBarHyperlink("Release notes", ActionNotes));
        if (!isRequired)
            actions.Add(new InfoBarHyperlink("Skip this version", ActionSkip));

        var model = new InfoBarModel(
            textSpans: new[] { new InfoBarTextSpan(label) },
            actionItems: actions.ToArray(),
            image: KnownMonikers.StatusInformation,
            isCloseButtonVisible: !isRequired);

        var element = factory.CreateInfoBar(model);
        var handler = new Handler(manifest);
        element.Advise(handler, out var cookie);
        handler.Cookie = cookie;
        host.AddInfoBar(element);
    }

    private sealed class Handler : IVsInfoBarUIEvents
    {
        private readonly VersionManifest _manifest;
        public uint Cookie;

        public Handler(VersionManifest manifest) { _manifest = manifest; }

        public void OnClosed(IVsInfoBarUIElement infoBarUIElement)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try { infoBarUIElement.Unadvise(Cookie); } catch { }
        }

        public void OnActionItemClicked(IVsInfoBarUIElement infoBarUIElement, IVsInfoBarActionItem actionItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            switch (actionItem.ActionContext as string)
            {
                case ActionDownload:
                    LaunchInstaller(_manifest.Url);
                    infoBarUIElement.Close();
                    break;

                case ActionNotes:
                    ShowReleaseNotes(_manifest);
                    break;

                case ActionSkip:
                    var s = SQLExtendedSettings.Load();
                    s.UpdateSkippedVersion = _manifest.Version;
                    s.Save();
                    infoBarUIElement.Close();
                    break;
            }
        }

        private static void LaunchInstaller(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                // Hand off to the user's browser. The feed points at the .vsix, which they run through
                // VSIXInstaller once SSMS is closed — unlike the Inno installer this replaced, nothing
                // closes SSMS for them, which is why the InfoBar text says to close it first.
                // Downloading-then-launching in-process is also possible but the browser flow is
                // simpler and matches what most SSMS extensions do.
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SQLExtendedLog.Error(nameof(UpdateCheckService), $"Launching the download failed for {url}", ex);
                Debug.WriteLine($"[SQLExtended] Launching installer failed: {ex}");
            }
        }

        private static void ShowReleaseNotes(VersionManifest manifest)
        {
            // Lightweight: write notes to a temp .txt and open with the default handler.
            // Replace with a proper WPF window later if the notes ever grow rich.
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"SQLExtended-{manifest.Version}-notes.txt");
                File.WriteAllText(path, $"SQLExtended for SSMS {manifest.Version}{Environment.NewLine}{Environment.NewLine}{manifest.Notes}");
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SQLExtended] Showing release notes failed: {ex}");
            }
        }
    }
}
