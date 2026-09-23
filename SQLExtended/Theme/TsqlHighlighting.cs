using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using SQLExtended.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace SQLExtended.Theme;

/// <summary>
/// The T-SQL highlighting for every AvalonEdit preview, in the variant that matches the shell.
///
/// <para>The embedded <c>TsqlDarkHighlighting.xshd</c> stays the one definition of what gets coloured; the light
/// variant is the same file with its foregrounds swapped for the VS light editor's, done on the text before it is
/// parsed so there is no second rule set to drift. AvalonEdit colours are values, not resource references, so a
/// theme switch has to re-apply — <see cref="Attach"/> does that for as long as the editor is loaded.</para>
/// </summary>
internal static class TsqlHighlighting
{
    private const string ResourceName = "SQLExtended.Search.TsqlDarkHighlighting.xshd";

    /// <summary>Dark foreground in the .xshd → the VS light editor's colour for the same role.</summary>
    private static readonly Dictionary<string, string> LightForegrounds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#6A9955"] = "#008000", // comment
        ["#CE9178"] = "#A31515", // string
        ["#569CD6"] = "#0000FF", // keyword
        ["#DCDCAA"] = "#795E26", // function
        ["#4EC9B0"] = "#267F99", // data type
        ["#B5CEA8"] = "#098658", // number
        ["#D4D4D4"] = "#1E1E1E", // operator
        ["#9CDCFE"] = "#001080", // variable
        ["#C586C0"] = "#AF00DB", // system view
        ["#D7BA7D"] = "#8A6100", // temp table
    };

    private static IHighlightingDefinition _dark, _light;

    public static IHighlightingDefinition Current => ThemeManager.IsDark ? (_dark ??= Load(dark: true)) : (_light ??= Load(dark: false));

    /// <summary>Sets the highlighting on each editor now and again on every theme switch while the first editor is loaded.</summary>
    public static void Attach(params TextEditor[] editors)
    {
        void Set(object sender, EventArgs e)
        {
            var definition = Current;
            if (definition == null) return;

            foreach (var editor in editors)
                editor.SyntaxHighlighting = definition;
        }

        Set(null, EventArgs.Empty);

        // Held only while loaded: the event is static, and a closed dialog must not stay reachable through it.
        var owner = editors[0];
        ThemeManager.Changed += Set;
        owner.Loaded += (_, _) => { ThemeManager.Changed -= Set; ThemeManager.Changed += Set; };
        owner.Unloaded += (_, _) => ThemeManager.Changed -= Set;
    }

    private static IHighlightingDefinition Load(bool dark)
    {
        try
        {
            using var stream = typeof(TsqlHighlighting).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                SQLExtendedLog.Error("Theme", $"Embedded resource {ResourceName} is missing; SQL previews will be uncoloured.");
                return null;
            }

            using var text = new StreamReader(stream);
            string xshd = text.ReadToEnd();

            if (!dark)
            {
                foreach (var pair in LightForegrounds)
                    xshd = xshd.Replace($"foreground=\"{pair.Key}\"", $"foreground=\"{pair.Value}\"");
            }

            using var reader = new XmlTextReader(new StringReader(xshd));
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception ex)
        {
            SQLExtendedLog.Error("Theme", "Could not load the T-SQL highlighting.", ex);
            return null;
        }
    }
}
