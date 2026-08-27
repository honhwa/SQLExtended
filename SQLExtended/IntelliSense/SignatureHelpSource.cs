using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using SQLExtended.Cache;
using SQLExtended.Cache.Models;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;

namespace SQLExtended.IntelliSense;

/// <summary>
/// Provides parameter signature hints when the cursor is inside a stored procedure
/// or function call. Shows parameter names, types, and highlights the current parameter.
/// </summary>
internal sealed class SignatureHelpSource : ISignatureHelpSource
{
    private readonly ITextBuffer _textBuffer;
    private bool _disposed;

    public SignatureHelpSource(ITextBuffer textBuffer)
    {
        _textBuffer = textBuffer;
    }

    public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
    {
        if (_disposed)
            return;

        try
        {
            var snapshot = _textBuffer.CurrentSnapshot;
            int position = session.GetTriggerPoint(_textBuffer).GetPosition(snapshot);
            string fullText = snapshot.GetText();

            var callInfo = SignatureHelpParser.ParseCallAtCursor(fullText, position);
            if (callInfo == null)
                return;

            // Built-in T-SQL functions (GETDATE, DATEADD, STRING_SPLIT, …) are intrinsic
            // to the language — resolve them from the catalog with no connection required.
            // They can't be schema-qualified, so only match unqualified calls.
            if (callInfo.Schema == null)
            {
                var builtIn = SqlBuiltInFunctions.Find(callInfo.ObjectName);
                if (builtIn != null && builtIn.RequiresParentheses && builtIn.Parameters.Count > 0)
                {
                    var span = snapshot.CreateTrackingSpan(
                        new Span(callInfo.ParametersStart, position - callInfo.ParametersStart),
                        SpanTrackingMode.EdgeInclusive);
                    signatures.Add(new BuiltInFunctionSignature(builtIn, span, callInfo.CurrentParameterIndex));
                    return;
                }
            }

            string connectionString = null;
            string currentDb = null;

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                connectionString = ConnectionHelper.GetActiveConnectionString();
                currentDb = ConnectionHelper.GetCurrentDatabaseName();
            });

            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(currentDb))
                return;

            var cache = SchemaCache.Instance;
            string connKey = cache.GetConnectionKey(connectionString);

            var obj = cache.FindObject(connKey, currentDb, callInfo.Schema, callInfo.ObjectName);
            if (obj == null)
                return;

            string type = obj.ObjectType?.Trim();
            if (type != "P" && type != "FN" && type != "IF" && type != "TF")
                return;

            var parameters = cache.GetParameters(connKey, currentDb, obj.SchemaName, obj.ObjectName);
            if (parameters == null || parameters.Count == 0)
                return;

            var applicableTo = snapshot.CreateTrackingSpan(
                new Span(callInfo.ParametersStart, position - callInfo.ParametersStart),
                SpanTrackingMode.EdgeInclusive);

            var signature = new SqlSignature(
                obj.SchemaName, obj.ObjectName, obj.ObjectTypeDisplay,
                parameters, applicableTo, callInfo.CurrentParameterIndex);

            signatures.Add(signature);
        }
        catch
        {
            // Never crash SSMS
        }
    }

    public ISignature GetBestMatch(ISignatureHelpSession session)
    {
        if (session.Signatures.Count > 0)
            return session.Signatures[0];
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

/// <summary>
/// Represents a single signature (parameter list) for a stored procedure or function.
/// </summary>
internal sealed class SqlSignature : ISignature
{
    private IParameter _currentParameter;

    public SqlSignature(
        string schemaName, string objectName, string objectTypeDisplay,
        IReadOnlyList<CachedParameter> parameters,
        ITrackingSpan applicableToSpan,
        int currentParameterIndex)
    {
        ApplicableToSpan = applicableToSpan;

        var paramParts = new List<string>();
        var vsParams = new List<IParameter>();
        int contentStart = $"{schemaName}.{objectName}(".Length;

        foreach (var p in parameters.OrderBy(p => p.Ordinal))
        {
            string part = FormatParameter(p);
            paramParts.Add(part);
        }

        Content = $"{schemaName}.{objectName}({string.Join(", ", paramParts)})";
        Documentation = objectTypeDisplay;
        PrettyPrintedContent = Content;

        int offset = contentStart;
        for (int i = 0; i < paramParts.Count; i++)
        {
            var span = new Span(offset, paramParts[i].Length);
            var locus = new Span(offset, paramParts[i].Length);
            var param = new SqlParameter(
                paramParts[i],
                FormatParameterDoc(parameters.OrderBy(p => p.Ordinal).ElementAt(i)),
                locus, span, this);
            vsParams.Add(param);
            offset += paramParts[i].Length + 2; // +2 for ", "
        }

        Parameters = new ReadOnlyCollection<IParameter>(vsParams);

        if (currentParameterIndex >= 0 && currentParameterIndex < Parameters.Count)
            _currentParameter = Parameters[currentParameterIndex];
        else if (Parameters.Count > 0)
            _currentParameter = Parameters[0];
    }

    public ITrackingSpan ApplicableToSpan { get; }
    public string Content { get; }
    public string Documentation { get; }
    public ReadOnlyCollection<IParameter> Parameters { get; }
    public string PrettyPrintedContent { get; }

    public IParameter CurrentParameter
    {
        get => _currentParameter;
        internal set
        {
            if (_currentParameter != value)
            {
                var old = _currentParameter;
                _currentParameter = value;
                CurrentParameterChanged?.Invoke(this,
                    new CurrentParameterChangedEventArgs(old, value));
            }
        }
    }

    public event EventHandler<CurrentParameterChangedEventArgs> CurrentParameterChanged;

    private static string FormatParameter(CachedParameter p)
    {
        string type = p.DataType ?? "unknown";
        string output = p.IsOutput ? " OUTPUT" : "";
        string def = p.HasDefault ? " = default" : "";
        return $"{p.ParameterName} {type}{output}{def}";
    }

    private static string FormatParameterDoc(CachedParameter p)
    {
        string type = p.DataType ?? "unknown";
        var parts = new List<string> { type };
        if (p.IsOutput) parts.Add("OUTPUT");
        if (p.HasDefault) parts.Add("has default value");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// Signature for a built-in T-SQL function, sourced from the SqlBuiltInFunctions
/// catalog rather than the schema cache. Highlights the current parameter and shows
/// the function's description as documentation.
/// </summary>
internal sealed class BuiltInFunctionSignature : ISignature
{
    private IParameter _currentParameter;

    public BuiltInFunctionSignature(SqlBuiltInFunction fn, ITrackingSpan applicableToSpan, int currentParameterIndex)
    {
        ApplicableToSpan = applicableToSpan;

        var paramParts = fn.Parameters.Select(p => p.Display).ToList();
        Content = $"{fn.Name}({string.Join(", ", paramParts)})";
        Documentation = $"{fn.Category} function · returns {fn.ReturnType}\n{fn.Description}";
        PrettyPrintedContent = Content;

        var vsParams = new List<IParameter>();
        int offset = fn.Name.Length + 1; // position just after the '('
        for (int i = 0; i < paramParts.Count; i++)
        {
            var locus = new Span(offset, paramParts[i].Length);
            string doc = fn.Parameters[i].IsOptional ? "Optional parameter." : "Parameter.";
            vsParams.Add(new SqlParameter(paramParts[i], doc, locus, locus, this));
            offset += paramParts[i].Length + 2; // +2 for ", "
        }

        Parameters = new ReadOnlyCollection<IParameter>(vsParams);

        if (currentParameterIndex >= 0 && currentParameterIndex < Parameters.Count)
            _currentParameter = Parameters[currentParameterIndex];
        else if (Parameters.Count > 0)
            _currentParameter = Parameters[0];
    }

    public ITrackingSpan ApplicableToSpan { get; }
    public string Content { get; }
    public string Documentation { get; }
    public ReadOnlyCollection<IParameter> Parameters { get; }
    public string PrettyPrintedContent { get; }

    public IParameter CurrentParameter
    {
        get => _currentParameter;
        internal set
        {
            if (_currentParameter != value)
            {
                var old = _currentParameter;
                _currentParameter = value;
                CurrentParameterChanged?.Invoke(this, new CurrentParameterChangedEventArgs(old, value));
            }
        }
    }

    public event EventHandler<CurrentParameterChangedEventArgs> CurrentParameterChanged;
}

/// <summary>
/// Represents a single parameter within a signature.
/// </summary>
internal sealed class SqlParameter : IParameter
{
    public SqlParameter(string name, string documentation, Span locus, Span prettyPrintedLocus, ISignature signature)
    {
        Name = name;
        Documentation = documentation;
        Locus = locus;
        PrettyPrintedLocus = prettyPrintedLocus;
        Signature = signature;
    }

    public string Name { get; }
    public string Documentation { get; }
    public Span Locus { get; }
    public Span PrettyPrintedLocus { get; }
    public ISignature Signature { get; }
}
