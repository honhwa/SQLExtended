using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class SignatureHelpParsingTests
{
    // --- ParseCallAtCursor: EXEC patterns ---

    [Fact]
    public void ParseCall_ExecWithParams_FindsProcAndParamIndex()
    {
        string sql = "EXEC dbo.usp_GetOrders @CustomerID = 1, @StartDate = ";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal("dbo", result.Schema);
        Assert.Equal("usp_GetOrders", result.ObjectName);
        Assert.Equal(1, result.CurrentParameterIndex);
    }

    [Fact]
    public void ParseCall_ExecFirstParam_ReturnsIndex0()
    {
        string sql = "EXEC dbo.usp_GetOrders @CustomerID";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal("dbo", result.Schema);
        Assert.Equal("usp_GetOrders", result.ObjectName);
        Assert.Equal(0, result.CurrentParameterIndex);
    }

    [Fact]
    public void ParseCall_ExecNoSchema_FindsProc()
    {
        string sql = "EXEC usp_GetOrders @ID = 1, ";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Null(result.Schema);
        Assert.Equal("usp_GetOrders", result.ObjectName);
        Assert.Equal(1, result.CurrentParameterIndex);
    }

    [Fact]
    public void ParseCall_ExecuteKeyword_Works()
    {
        string sql = "EXECUTE dbo.usp_Test @A = 1, @B = 2, @C = ";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal("usp_Test", result.ObjectName);
        Assert.Equal(2, result.CurrentParameterIndex);
    }

    // --- ParseCallAtCursor: Function with parentheses ---

    [Fact]
    public void ParseCall_FunctionWithParens_FindsFunction()
    {
        string sql = "SELECT dbo.fn_GetName(1, ";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal("dbo", result.Schema);
        Assert.Equal("fn_GetName", result.ObjectName);
        Assert.Equal(1, result.CurrentParameterIndex);
    }

    [Fact]
    public void ParseCall_FunctionFirstParam_ReturnsIndex0()
    {
        string sql = "SELECT dbo.fn_GetName(";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal("fn_GetName", result.ObjectName);
        Assert.Equal(0, result.CurrentParameterIndex);
    }

    [Fact]
    public void ParseCall_NestedParens_CorrectParamIndex()
    {
        string sql = "SELECT dbo.fn_Calc(ISNULL(a, 0), ";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.NotNull(result);
        Assert.Equal("fn_Calc", result.ObjectName);
        Assert.Equal(1, result.CurrentParameterIndex);
    }

    [Fact]
    public void ParseCall_BuiltInKeyword_ReturnsNull()
    {
        string sql = "IF (";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.Null(result);
    }

    [Fact]
    public void ParseCall_NoCall_ReturnsNull()
    {
        string sql = "SELECT * FROM dbo.Customers";
        var result = SignatureHelpParser.ParseCallAtCursor(sql, sql.Length);
        Assert.Null(result);
    }

    // --- CountParameters ---

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("@A = 1", 0)]
    [InlineData("@A = 1, @B = 2", 1)]
    [InlineData("@A = 1, @B = 2, @C = 3", 2)]
    public void CountParameters_ReturnsCorrectIndex(string paramText, int expected)
    {
        int result = SignatureHelpParser.CountParameters(paramText);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CountParameters_IgnoresCommasInStrings()
    {
        string paramText = "@A = 'hello, world', @B = 2";
        int result = SignatureHelpParser.CountParameters(paramText);
        Assert.Equal(1, result);
    }

    [Fact]
    public void CountParameters_IgnoresCommasInNestedParens()
    {
        string paramText = "@A = ISNULL(x, 0), @B = 2";
        int result = SignatureHelpParser.CountParameters(paramText);
        Assert.Equal(1, result);
    }
}
