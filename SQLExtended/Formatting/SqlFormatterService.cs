using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SQLExtended.Formatting;

/// <summary>
/// Formats T-SQL using ScriptDom's ScriptGenerator as baseline, then post-processes
/// for options that ScriptGenerator doesn't natively support.
/// </summary>
public class SqlFormatterService
{
    private readonly FormatterOptions _options;

    public SqlFormatterService(FormatterOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Formats the given SQL text. Returns the original text if parsing fails.
    /// </summary>
    public FormatResult Format(string inputSql)
    {
        if (string.IsNullOrWhiteSpace(inputSql))
            return new FormatResult(inputSql, success: true);

        // Split on GO batches and format each independently
        var batches = SplitOnGo(inputSql);
        if (batches.Count > 1)
            return FormatBatches(batches);

        return FormatSingle(inputSql);
    }

    private FormatResult FormatSingle(string inputSql)
    {
        var parser = new TSql170Parser(initialQuotedIdentifiers: true);
        IList<ParseError> errors;
        TSqlFragment fragment;

        using (var reader = new StringReader(inputSql))
        {
            fragment = parser.Parse(reader, out errors);
        }

        if (errors != null && errors.Count > 0)
        {
            return new FormatResult(inputSql, success: false,
                errorMessage: $"Parse error at line {errors[0].Line}, col {errors[0].Column}: {errors[0].Message}");
        }

        // Step 1: Use ScriptGenerator for baseline formatting (PreserveComments keeps comments)
        var generatorOptions = BuildGeneratorOptions();
        var generator = new Sql170ScriptGenerator(generatorOptions);
        string baseline;
        generator.GenerateScript(fragment, out baseline);

        // Step 2: Post-process for options ScriptGenerator doesn't support. The source's trailing comments
        // go with it: the generated text cannot say which comments were trailing, and PostProcessor has to
        // know before it moves any of them. See RejoinInlineComments.
        string result = PostProcessor.Apply(baseline, _options, CollectTrailingComments(fragment.ScriptTokenStream));

        return new FormatResult(result, success: true);
    }

    /// <summary>
    /// The single-line comments the source carried at the end of a line of code, counted by text. This is
    /// the only place that knowledge exists — ScriptDom's generator emits a trailing comment and an
    /// own-line comment the same way, so by the time <see cref="PostProcessor"/> sees the text the
    /// distinction is gone. Counted rather than collected into a set because the same note repeated down a
    /// column list ("-- from main") is the shape this was written for.
    /// </summary>
    internal static Dictionary<string, int> CollectTrailingComments(IList<TSqlParserToken> tokens)
    {
        var trailing = new Dictionary<string, int>(StringComparer.Ordinal);
        if (tokens == null)
            return trailing;

        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].TokenType != TSqlTokenType.SingleLineComment)
                continue;

            // Walk back over the same line. Whitespace and comments are not code; a token that starts on
            // an earlier line means the start of this line was reached, so there was no code in front.
            bool hasCodeBefore = false;
            for (int j = i - 1; j >= 0; j--)
            {
                var token = tokens[j];
                if (token.Line != tokens[i].Line)
                    break;
                if (token.TokenType == TSqlTokenType.WhiteSpace ||
                    token.TokenType == TSqlTokenType.SingleLineComment ||
                    token.TokenType == TSqlTokenType.MultilineComment)
                    continue;

                hasCodeBefore = true;
                break;
            }

            if (!hasCodeBefore)
                continue;

            string text = tokens[i].Text?.Trim();
            if (string.IsNullOrEmpty(text))
                continue;

            trailing.TryGetValue(text, out int count);
            trailing[text] = count + 1;
        }

        return trailing;
    }

    private FormatResult FormatBatches(List<BatchSegment> batches)
    {
        var sb = new StringBuilder();
        var allErrors = new List<string>();
        bool anySuccess = false;

        for (int i = 0; i < batches.Count; i++)
        {
            var batch = batches[i];

            if (batch.IsGo)
            {
                // Preserve GO lines
                sb.Append("GO");
                if (i < batches.Count - 1)
                {
                    for (int b = 0; b <= _options.BlankLineAfterGO; b++)
                        sb.AppendLine();
                }
                continue;
            }

            var result = FormatSingle(batch.Text);
            sb.Append(result.FormattedSql.TrimEnd());

            if (result.Success)
                anySuccess = true;
            else if (result.ErrorMessage != null)
                allErrors.Add(result.ErrorMessage);

            // Add blank lines between statements (before GO or next batch)
            if (i < batches.Count - 1)
            {
                sb.AppendLine();
                for (int b = 0; b < _options.BlankLinesBetweenStatements; b++)
                    sb.AppendLine();
            }
        }

        string errorMsg = allErrors.Count > 0 ? string.Join("; ", allErrors) : null;
        return new FormatResult(sb.ToString(), success: anySuccess || allErrors.Count == 0, errorMessage: errorMsg);
    }

    private SqlScriptGeneratorOptions BuildGeneratorOptions()
    {
        var opts = new SqlScriptGeneratorOptions
        {
            IndentationSize = _options.IndentSize,
            SqlVersion = SqlVersion.Sql170,

            // Clause layout
            AlignClauseBodies = _options.SelectColumnLayout == SelectColumnLayoutOption.StackedAligned,
            NewLineBeforeFromClause = true,
            NewLineBeforeWhereClause = true,
            NewLineBeforeGroupByClause = true,
            NewLineBeforeOrderByClause = true,
            NewLineBeforeHavingClause = true,
            NewLineBeforeJoinClause = _options.JoinLayout == JoinLayoutOption.NewLine,
            NewLineBeforeOutputClause = true,
            NewLineBeforeOffsetClause = _options.NewLineBeforeOffsetClause,
            NewLineBeforeWindowClause = _options.NewLineBeforeWindowClause,

            // Multiline lists
            MultilineSelectElementsList = _options.SelectColumnLayout != SelectColumnLayoutOption.SameLine,
            MultilineInsertSourcesList = true,
            MultilineInsertTargetsList = true,
            MultilineWherePredicatesList = _options.WhereConditionLayout == WhereConditionLayoutOption.NewLinePerCondition,
            MultilineViewColumnsList = _options.MultilineViewColumnsList,

            // Parenthesis in multiline lists
            NewLineBeforeOpenParenthesisInMultilineList = _options.NewLineBeforeOpenParenthesis,
            NewLineBeforeCloseParenthesisInMultilineList = _options.NewLineBeforeCloseParenthesis,

            // UPDATE / SET
            AlignSetClauseItem = _options.AlignSetClauseItem,
            MultilineSetClauseItems = _options.MultilineSetClauseItems,
            IndentSetClause = true,

            // DDL formatting
            AlignColumnDefinitionFields = _options.AlignColumnDefinitionFields,
            NewlineFormattedCheckConstraint = _options.NewlineFormattedCheckConstraint,
            NewLineFormattedIndexDefinition = _options.NewLineFormattedIndexDefinition,

            // AS keyword (CTEs, derived tables)
            AsKeywordOnOwnLine = _options.AsKeywordOnOwnLine,

            // Data type spacing
            SpaceBetweenDataTypeAndParameters = _options.SpaceBetweenDataTypeAndParameters,
            SpaceBetweenParametersInDataType = _options.SpaceBetweenParametersInDataType,

            // View body
            IndentViewBody = _options.IndentViewBody,

            // Semicolons & blank lines
            IncludeSemicolons = _options.TrailingSemicolon == SemicolonOption.Always,
            NumNewlinesAfterStatement = _options.BlankLinesBetweenStatements,
            PreserveComments = true,
        };

        switch (_options.KeywordCase)
        {
            case CasingOption.Upper:
                opts.KeywordCasing = KeywordCasing.Uppercase;
                break;
            case CasingOption.Lower:
                opts.KeywordCasing = KeywordCasing.Lowercase;
                break;
            case CasingOption.Unchanged:
                opts.KeywordCasing = KeywordCasing.PascalCase;
                break;
        }

        return opts;
    }

    /// <summary>
    /// Splits SQL text on GO batch separators, preserving each segment.
    /// </summary>
    private static List<BatchSegment> SplitOnGo(string sql)
    {
        var segments = new List<BatchSegment>();
        var lines = sql.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var currentBatch = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase) && line.Trim().Length == 2)
            {
                if (currentBatch.Length > 0)
                {
                    segments.Add(new BatchSegment(currentBatch.ToString(), isGo: false));
                    currentBatch.Clear();
                }
                segments.Add(new BatchSegment("GO", isGo: true));
            }
            else
            {
                if (currentBatch.Length > 0)
                    currentBatch.AppendLine();
                currentBatch.Append(line);
            }
        }

        if (currentBatch.Length > 0)
            segments.Add(new BatchSegment(currentBatch.ToString(), isGo: false));

        return segments;
    }

    private class BatchSegment
    {
        public string Text { get; }
        public bool IsGo { get; }

        public BatchSegment(string text, bool isGo)
        {
            Text = text;
            IsGo = isGo;
        }
    }
}

public class FormatResult
{
    public string FormattedSql { get; }
    public bool Success { get; }
    public string ErrorMessage { get; }

    public FormatResult(string formattedSql, bool success, string errorMessage = null)
    {
        FormattedSql = formattedSql;
        Success = success;
        ErrorMessage = errorMessage;
    }
}
