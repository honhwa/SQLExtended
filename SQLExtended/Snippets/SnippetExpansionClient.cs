using System;
using SQLExtended.IntelliSense;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using MSXML;

namespace SQLExtended.Snippets;

/// <summary>
/// Handles VS snippet expansion session lifecycle. Implements <see cref="IVsExpansionClient"/>
/// to receive callbacks during tab-stop navigation (field changes, commit, cancel).
/// </summary>
internal sealed class SnippetExpansionClient : IVsExpansionClient
{
    private readonly IVsTextView _vsTextView;
    private readonly ITextView _textView;
    private IVsExpansionSession _session;

    public SnippetExpansionClient(IVsTextView vsTextView, ITextView textView)
    {
        _vsTextView = vsTextView ?? throw new ArgumentNullException(nameof(vsTextView));
        _textView = textView ?? throw new ArgumentNullException(nameof(textView));
    }

    /// <summary>True while a snippet expansion session is active.</summary>
    public bool IsInExpansionSession => _session != null;

    /// <summary>
    /// Starts a snippet expansion by inserting the given snippet XML at <paramref name="replacementSpan"/>.
    /// The replacement span is typically the typed prefix text that triggered the completion.
    /// </summary>
    public bool StartExpansion(string snippetXml, SnapshotSpan replacementSpan)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        if (string.IsNullOrEmpty(snippetXml))
        {
            SqlCompletionSource.DebugLog("[Expansion] snippetXml is null/empty");
            return false;
        }

        // Get IVsExpansion from the text view
        if (!(_vsTextView is IVsExpansion expansion))
        {
            SqlCompletionSource.DebugLog($"[Expansion] IVsTextView does not support IVsExpansion. Type: {_vsTextView.GetType().FullName}");
            // List interfaces for diagnostics
            foreach (var iface in _vsTextView.GetType().GetInterfaces())
                SqlCompletionSource.DebugLog($"[Expansion]   interface: {iface.FullName}");
            return false;
        }

        SqlCompletionSource.DebugLog("[Expansion] IVsExpansion is available");

        // Convert SnapshotSpan to TextSpan
        var startLine = replacementSpan.Start.GetContainingLine();
        var endLine = replacementSpan.End.GetContainingLine();
        var textSpan = new TextSpan
        {
            iStartLine = startLine.LineNumber,
            iStartIndex = replacementSpan.Start.Position - startLine.Start.Position,
            iEndLine = endLine.LineNumber,
            iEndIndex = replacementSpan.End.Position - endLine.Start.Position,
        };

        SqlCompletionSource.DebugLog($"[Expansion] TextSpan: line {textSpan.iStartLine}:{textSpan.iStartIndex} to {textSpan.iEndLine}:{textSpan.iEndIndex}");

        // Parse the snippet XML into a DOM node using MSXML6 COM automation
        IXMLDOMNode snippetNode = ParseSnippetXml(snippetXml);
        if (snippetNode == null)
        {
            SqlCompletionSource.DebugLog("[Expansion] ParseSnippetXml returned null (MSXML6 failed)");
            return false;
        }

        SqlCompletionSource.DebugLog("[Expansion] MSXML DOM loaded successfully");

        // SQL language GUID (T-SQL)
        var sqlLanguageGuid = new Guid("11A15160-DE90-11D0-926A-0020AF71E433");

        int hr = expansion.InsertSpecificExpansion(
            snippetNode,
            textSpan,
            this,
            sqlLanguageGuid,
            string.Empty,
            out _session);

        SqlCompletionSource.DebugLog($"[Expansion] InsertSpecificExpansion HR=0x{hr:X8}, session={_session != null}");

        return ErrorHandler.Succeeded(hr) && _session != null;
    }

    /// <summary>
    /// Creates an MSXML6 DOMDocument via COM activation and loads the snippet XML.
    /// Uses dynamic dispatch to avoid a hard project-level COM reference on MSXML6.
    /// </summary>
    private static IXMLDOMNode ParseSnippetXml(string xml)
    {
        try
        {
            var type = Type.GetTypeFromProgID("Msxml2.DOMDocument.6.0");
            if (type == null)
                return null;

            dynamic doc = Activator.CreateInstance(type);
            doc.async = false;
            doc.validateOnParse = false;
            doc.resolveExternals = false;

            if (doc.loadXML(xml))
                return (IXMLDOMNode)doc;

            return null;
        }
        catch
        {
            return null;
        }
    }

    #region IVsExpansionClient

    public int EndExpansion()
    {
        _session = null;
        return VSConstants.S_OK;
    }

    public int FormatSpan(IVsTextLines pBuffer, TextSpan[] ts)
    {
        return VSConstants.S_OK;
    }

    public int GetExpansionFunction(IXMLDOMNode xmlFunctionNode, string bstrFieldName, out IVsExpansionFunction pFunc)
    {
        pFunc = null;
        return VSConstants.S_OK;
    }

    public int IsValidKind(IVsTextLines pBuffer, TextSpan[] ts, string bstrKind, out int pfIsValidKind)
    {
        pfIsValidKind = 1;
        return VSConstants.S_OK;
    }

    public int IsValidType(IVsTextLines pBuffer, TextSpan[] ts, string[] rgTypes, int iCountTypes, out int pfIsValidType)
    {
        pfIsValidType = 1;
        return VSConstants.S_OK;
    }

    public int OnAfterInsertion(IVsExpansionSession pSession)
    {
        return VSConstants.S_OK;
    }

    public int OnBeforeInsertion(IVsExpansionSession pSession)
    {
        return VSConstants.S_OK;
    }

    public int OnItemChosen(string pszTitle, string pszPath)
    {
        return VSConstants.S_OK;
    }

    public int PositionCaretForEditing(IVsTextLines pBuffer, TextSpan[] ts)
    {
        return VSConstants.S_OK;
    }

    #endregion
}
