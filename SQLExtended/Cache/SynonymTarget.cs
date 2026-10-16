using System.Collections.Generic;
using System.Text;

namespace SQLExtended.Cache;

/// <summary>
/// The four parts of a synonym's <c>sys.synonyms.base_object_name</c> — "[server].[db].[schema].[name]" with
/// any leading part left out (and "db..name" leaving the schema empty). Parsed the way PARSENAME does it:
/// brackets and double quotes delimit a part, "]]" inside brackets is a literal "]". Empty parts are null.
/// </summary>
internal sealed class SynonymTarget(string server, string database, string schema, string name)
{
    public string Server { get; } = server;
    public string Database { get; } = database;
    public string Schema { get; } = schema;
    public string Name { get; } = name;

    /// <summary>The parsed target, or null when the text is empty or has more than four parts.</summary>
    public static SynonymTarget Parse(string baseObjectName)
    {
        if (string.IsNullOrWhiteSpace(baseObjectName))
            return null;

        var parts = new List<string>();
        var current = new StringBuilder();
        string text = baseObjectName.Trim();  // sys.synonyms stores it fully bracketed, so no space sits outside a part
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '[' || c == '"')
            {
                char close = c == '[' ? ']' : '"';
                for (i++; i < text.Length; i++)
                {
                    if (text[i] == close)
                    {
                        if (i + 1 < text.Length && text[i + 1] == close) { current.Append(close); i++; continue; }
                        break;
                    }
                    current.Append(text[i]);
                }
            }
            else if (c == '.')
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        parts.Add(current.ToString());

        if (parts.Count > 4 || parts[parts.Count - 1].Length == 0)
            return null;

        string Part(int fromEnd) => parts.Count > fromEnd && parts[parts.Count - 1 - fromEnd].Length > 0 ? parts[parts.Count - 1 - fromEnd] : null;
        return new SynonymTarget(Part(3), Part(2), Part(1), Part(0));
    }
}
