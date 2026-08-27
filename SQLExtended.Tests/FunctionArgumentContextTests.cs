using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class FunctionArgumentContextTests
{
    private static SqlContextAnalyzer.AnalysisResult AnalyzeAtEnd(string sql)
        => SqlContextAnalyzer.Analyze(sql, sql.Length);

    // --- CONVERT: first argument is a data type ---

    [Theory]
    [InlineData("SELECT CONVERT(")]
    [InlineData("SELECT CONVERT(va")]
    [InlineData("SELECT TRY_CONVERT(")]
    [InlineData("WHERE x = CONVERT(")]
    public void Convert_FirstArg_IsDataType(string sql)
    {
        var result = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
        Assert.Equal(SqlArgKind.DataType, result.ArgumentKind);
    }

    [Fact]
    public void Convert_SecondArg_IsNotDataType()
    {
        // After the first comma we're at the value expression, not the type.
        string sql = "SELECT CONVERT(varchar(10), ";
        var result = AnalyzeAtEnd(sql);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
    }

    // --- CAST / PARSE: data type after AS ---

    [Theory]
    [InlineData("SELECT CAST(@x AS ")]
    [InlineData("SELECT CAST(@x AS in")]
    [InlineData("SELECT TRY_CAST(col AS ")]
    [InlineData("SELECT PARSE('1' AS ")]
    public void Cast_AfterAs_IsDataType(string sql)
    {
        var result = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
        Assert.Equal(SqlArgKind.DataType, result.ArgumentKind);
    }

    [Theory]
    [InlineData("SELECT CAST(")]        // before AS — expression position
    [InlineData("SELECT CAST(@x ")]      // value typed, AS not yet
    public void Cast_BeforeAs_IsNotDataType(string sql)
    {
        var result = AnalyzeAtEnd(sql);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
    }

    [Fact]
    public void Cast_AscColumnReference_DoesNotTriggerType()
    {
        // "ASC" must not be mistaken for the AS keyword.
        string sql = "SELECT CAST(ASC";
        var result = AnalyzeAtEnd(sql);
        Assert.NotEqual(SqlArgKind.DataType, result.ArgumentKind);
    }

    // --- DATEADD family: first argument is a datepart ---

    [Theory]
    [InlineData("SELECT DATEADD(")]
    [InlineData("SELECT DATEDIFF(")]
    [InlineData("SELECT DATEPART(")]
    [InlineData("SELECT DATENAME(")]
    [InlineData("SELECT DATETRUNC(")]
    [InlineData("SELECT DATEADD(ye")]
    public void DateFunctions_FirstArg_IsDatePart(string sql)
    {
        var result = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
        Assert.Equal(SqlArgKind.DatePart, result.ArgumentKind);
    }

    [Fact]
    public void Dateadd_SecondArg_IsNotDatePart()
    {
        string sql = "SELECT DATEADD(year, ";
        var result = AnalyzeAtEnd(sql);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
    }

    // --- Schema-qualified calls are user functions, not built-ins ---

    [Fact]
    public void SchemaQualifiedCall_IsNotFunctionArgument()
    {
        string sql = "SELECT dbo.CONVERT(";
        var result = AnalyzeAtEnd(sql);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
    }

    // --- Nested calls resolve to the innermost function ---

    [Fact]
    public void NestedCall_ResolvesInnermostFunction()
    {
        // Cursor inside DATEPART, which is nested inside CONVERT's value argument.
        string sql = "SELECT CONVERT(varchar, DATEPART(";
        var result = AnalyzeAtEnd(sql);
        Assert.Equal(SqlContextAnalyzer.CompletionType.FunctionArgument, result.Type);
        Assert.Equal(SqlArgKind.DatePart, result.ArgumentKind);
    }
}
