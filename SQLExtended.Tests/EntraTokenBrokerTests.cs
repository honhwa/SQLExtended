using Xunit;

namespace SQLExtended.Tests;

/// <summary>
/// The server key an Entra access token is filed under.
///
/// <para>Worth pinning because every way of getting this wrong looks the same from outside: the token is
/// harvested, nothing complains, and the connection that needed it is opened without one — failing at the far
/// end, on a background thread, as <c>Windows logins are not supported in this version of SQL Server</c>. The
/// same server genuinely arrives written several ways: <c>tcp:host,1433</c> from the connection dialog,
/// <c>host</c> from Object Explorer, <c>ADMIN:host</c> from a DAC window.</para>
/// </summary>
public class EntraTokenBrokerTests
{
    [Theory]
    [InlineData("cc-analytics.database.windows.net")]
    [InlineData("CC-Analytics.Database.Windows.Net")]
    [InlineData("tcp:cc-analytics.database.windows.net")]
    [InlineData("tcp:cc-analytics.database.windows.net,1433")]
    [InlineData("cc-analytics.database.windows.net,1433")]
    [InlineData("  cc-analytics.database.windows.net  ")]
    [InlineData("ADMIN:cc-analytics.database.windows.net")]
    public void EveryWayOfWritingTheSameServerSharesOneKey(string dataSource)
    {
        Assert.Equal("cc-analytics.database.windows.net", EntraTokenBroker.ServerKey(dataSource));
    }

    [Fact]
    public void DifferentServersDoNotShareAKey()
    {
        Assert.NotEqual(EntraTokenBroker.ServerKey("one.database.windows.net"), EntraTokenBroker.ServerKey("two.database.windows.net"));
    }

    [Fact]
    public void NamedInstancesKeepTheirInstanceName()
    {
        // The instance is part of the endpoint, not a port — collapsing SERVER\SQL2019 onto SERVER would hand a
        // token harvested from one instance to connections opened against another.
        Assert.Equal(@"server\sql2019", EntraTokenBroker.ServerKey(@"SERVER\SQL2019"));
    }

    [Fact]
    public void NothingIsKeyedWhenThereIsNoServer()
    {
        Assert.Equal("", EntraTokenBroker.ServerKey(null));
        Assert.Equal("", EntraTokenBroker.ServerKey("   "));
    }

    [Fact]
    public void AServerWithNoHarvestedTokenHasNone()
    {
        Assert.False(EntraTokenBroker.HasToken("never-harvested.database.windows.net"));
        Assert.Null(EntraTokenBroker.TryGetAccessToken("never-harvested.database.windows.net"));
    }
}
