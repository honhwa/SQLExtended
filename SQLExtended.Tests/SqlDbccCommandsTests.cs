using SQLExtended.IntelliSense;
using Xunit;

namespace SQLExtended.Tests;

public class SqlDbccCommandsTests
{
    [Theory]
    [InlineData("CHECKDB")]
    [InlineData("checkdb")]      // case-insensitive
    [InlineData("SHRINKFILE")]
    [InlineData("FREEPROCCACHE")]
    [InlineData("TRACEON")]
    [InlineData("PAGE")]
    public void Find_KnownCommand_ReturnsIt(string name)
    {
        var cmd = SqlDbccCommands.Find(name);
        Assert.NotNull(cmd);
        Assert.Equal(name, cmd.Name, ignoreCase: true);
    }

    [Theory]
    [InlineData("NOTACOMMAND")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_Unknown_ReturnsNull(string name)
    {
        Assert.Null(SqlDbccCommands.Find(name));
    }

    [Fact]
    public void AllCommands_HaveNameCategoryAndDescription()
    {
        foreach (var cmd in SqlDbccCommands.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(cmd.Name));
            Assert.False(string.IsNullOrWhiteSpace(cmd.Category));
            Assert.False(string.IsNullOrWhiteSpace(cmd.Description));
            Assert.NotNull(cmd.Syntax); // may be empty (e.g. USEROPTIONS) but never null
        }
    }

    // --- Context detection ---

    [Theory]
    [InlineData("DBCC ")]
    [InlineData("DBCC CHECK")]
    [InlineData("SELECT 1;\nDBCC ")]
    [InlineData("DBCC SHRINK")]
    public void Analyze_AfterDbcc_ReturnsDbccCommand(string sql)
    {
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.Equal(SqlContextAnalyzer.CompletionType.DbccCommand, result.Type);
    }

    [Fact]
    public void Analyze_DbccWithCommandAndArgs_IsNotCommandContext()
    {
        // Past the command name, into the arguments — no longer command-name completion.
        string sql = "DBCC CHECKDB (";
        var result = SqlContextAnalyzer.Analyze(sql, sql.Length);
        Assert.NotEqual(SqlContextAnalyzer.CompletionType.DbccCommand, result.Type);
    }
}
