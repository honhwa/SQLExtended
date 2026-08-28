using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;
using SQLExtended.Diagnostics;
using SQLExtended.Settings;
using System;
using System.Windows.Media;

namespace SQLExtended.Comments;

/// <summary>
/// Writes a <see cref="CommentScheme"/> into Fonts and Colors.
///
/// <para><b>Why this exists at all.</b> The colours on the format definitions are only defaults: the moment
/// SSMS has a stored value for a classification — which it does as soon as the user has opened Fonts and
/// Colors, and after any theme switch — that stored value wins. So a scheme cannot be switched by changing
/// what the format definitions declare. It has to be written into the format map, which is the same store
/// the Fonts and Colors dialog edits, and it persists the same way.</para>
///
/// <para><b>When it writes.</b> Only when the wanted scheme and variant differ from what was last written,
/// recorded in <see cref="SQLExtendedSettings.CommentSchemeApplied"/>. So: once on first run, again when the
/// user picks a different scheme, and again when the editor flips between a dark and a light theme. An
/// unchanged setup is never rewritten, which is what leaves hand-tuning in Fonts and Colors alone —
/// retune a colour, and it survives every restart until you deliberately change scheme.</para>
///
/// <para>It only ever writes this feature's own sixteen classifications. Nothing else in Fonts and Colors
/// is touched.</para>
/// </summary>
internal static class CommentThemeApplier
{
    /// <summary>
    /// Guards the save below from re-entering through <see cref="SQLExtendedSettings.Changed"/>, which is
    /// what this method is usually called from. Without it, recording the applied scheme raises the event
    /// that called it.
    /// </summary>
    private static bool _applying;

    private static bool _subscribed;

    /// <summary>
    /// Applies the configured scheme if it is not already the one in force, and starts listening for theme
    /// changes. Call once from package initialization, on the UI thread.
    /// </summary>
    public static void Initialize()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (!_subscribed)
        {
            // A dark-to-light switch needs the other variant of the same scheme. The event is static and
            // lives as long as the shell, so there is nothing to unsubscribe from — this is process-wide
            // state, not per-window, unlike the taggers.
            VSColorTheme.ThemeChanged += OnThemeChanged;

            // Picking a scheme in the settings dialog arrives here, rather than the dialog calling in.
            // Every path that saves settings then applies, and the guard above stops the save this makes
            // from coming back round.
            SQLExtendedSettings.Changed += OnSettingsChanged;
            _subscribed = true;
        }

        ApplyIfNeeded();
    }

    private static void OnThemeChanged(ThemeChangedEventArgs e) => ApplyIfNeeded();

    private static void OnSettingsChanged(object sender, EventArgs e)
    {
        // Save() raises this on whichever thread called it. The dialog's OK is the UI thread, but nothing
        // guarantees that of every caller, and the format map is UI-thread only.
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ApplyIfNeeded();
        });
    }

    /// <summary>Writes the configured scheme only if it differs from the one last written. UI thread only.</summary>
    public static void ApplyIfNeeded()
    {
        if (_applying)
            return;

        try
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var settings = SQLExtendedSettings.Current;
            if (!settings.CommentTagsEnabled)
                return;

            bool dark = IsDarkTheme();
            string wanted = $"{settings.CommentScheme}/{(dark ? "dark" : "light")}";

            if (string.Equals(settings.CommentSchemeApplied, wanted, StringComparison.Ordinal))
                return;

            if (!Apply(settings.CommentScheme, dark))
                return;

            _applying = true;
            try
            {
                settings.CommentSchemeApplied = wanted;
                settings.Save();
            }
            finally
            {
                _applying = false;
            }

            SQLExtendedLog.Info("Comments", $"Applied the '{CommentThemes.DisplayName(settings.CommentScheme)}' comment scheme ({(dark ? "dark" : "light")}).");
        }
        catch (Exception ex)
        {
            // Every failure in this feature is silent on screen, so it has to be said somewhere.
            SQLExtendedLog.Error("Comments", "Could not apply the comment colour scheme.", ex);
        }
    }

    /// <summary>
    /// Writes one palette into the "text" classification format map. Returns false when the editor services
    /// are not available — which happens if this runs before the shell has composed them.
    /// </summary>
    public static bool Apply(CommentScheme scheme, bool dark)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (Package.GetGlobalService(typeof(SComponentModel)) is not IComponentModel components)
            return false;

        var formatMapService = components.GetService<IClassificationFormatMapService>();
        var registry = components.GetService<IClassificationTypeRegistryService>();

        if (formatMapService == null || registry == null)
            return false;

        var map = formatMapService.GetClassificationFormatMap("text");
        if (map == null)
            return false;

        var palette = CommentThemes.Palette(scheme, dark);

        // One batch, so the editor re-renders once rather than sixteen times — and so a failure part way
        // through does not leave half a scheme on screen.
        map.BeginBatchUpdate();
        try
        {
            for (int i = 0; i < CommentClassifications.AllNames.Length && i < palette.Length; i++)
            {
                var type = registry.GetClassificationType(CommentClassifications.AllNames[i]);
                if (type == null)
                    continue;

                var properties = map.GetTextProperties(type)
                    .SetForeground(ToColor(palette[i]))
                    .SetBold(CommentThemes.IsBold((CommentMarkKind)i));

                map.SetTextProperties(type, properties);
            }
        }
        finally
        {
            map.EndBatchUpdate();
        }

        return true;
    }

    private static Color ToColor(uint rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>
    /// Whether the editor is on a dark background.
    ///
    /// <para>Measured from the tool-window background rather than matched against the names of the stock
    /// themes: a user on a third-party or hand-edited theme still gets the right variant, and that was an
    /// open question worth closing rather than a case to fail on. The threshold is plain luminance — the
    /// two variants only have to be told apart, not graded.</para>
    /// </summary>
    private static bool IsDarkTheme()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        try
        {
            var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);
            double luminance = (0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B);

            return luminance < 128;
        }
        catch (Exception ex)
        {
            // Dark is the SSMS default here, so it is the safer guess when the shell will not say.
            SQLExtendedLog.Error("Comments", "Could not read the editor theme; assuming dark.", ex);
            return true;
        }
    }
}
