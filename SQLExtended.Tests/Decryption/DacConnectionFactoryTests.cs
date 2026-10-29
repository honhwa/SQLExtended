using System;
using SQLExtended.Decryption;
using Xunit;

namespace SQLExtended.Tests.Decryption;

/// <summary>
/// Which DAC refusals are worth retrying, and which message a reader gets for each. This is the part of the
/// feature that decides whether a single collision with an <c>ADMIN:</c> query window looks like a moment or
/// like a broken extension for the rest of the session, and it is checkable without a server because the
/// classification reads the error text.
/// </summary>
public class DacConnectionFactoryTests
{
    /// <summary>What SQL Server 17810 actually says.</summary>
    private const string Busy =
        "Could not connect because the maximum number of '1' dedicated administrator connections already exists. " +
        "Before a new connection may be established, you must first disconnect the existing dedicated administrator connection.";

    /// <summary>Azure SQL Database's refusal. It contains "dedicated administrator connections" and means the opposite of busy.</summary>
    private const string Azure =
        "Cannot connect to SQL Database. Dedicated administrator connections are not supported by this edition of SQL Server.";

    [Fact]
    public void ABusyDacIsTransient()
    {
        Assert.True(DacConnectionFactory.IsBusy(new Exception(Busy)));
    }

    [Fact]
    public void AWaitingSessionIsMatchedWithoutTheExactWording()
    {
        Assert.True(DacConnectionFactory.IsBusy(new Exception("The dedicated administrator connection already exists. Disconnect it first.")));
    }

    /// <summary>
    /// The regression this guards: Azure's refusal was matched by the same substring as the busy one, so it was
    /// retried on every refresh and reported as "another DAC session is holding it" — advice that cannot work.
    /// </summary>
    [Fact]
    public void AnUnsupportedDacIsNotBusy()
    {
        Assert.False(DacConnectionFactory.IsBusy(new Exception(Azure)));
    }

    [Fact]
    public void ALoginFailureIsNotBusy()
    {
        Assert.False(DacConnectionFactory.IsBusy(new Exception("Login failed for user 'reporting'. Reason: The account is disabled.")));
    }

    [Fact]
    public void BusyIsReportedAsSomeoneElsesSessionWithAWayToFindIt()
    {
        string message = DacConnectionFactory.Explain(new Exception(Busy));

        Assert.Contains("already in use", message);
        Assert.Contains("Dedicated Admin Connection", message);
    }

    [Fact]
    public void UnsupportedIsReportedAsUnsupportedAndNotAsBusy()
    {
        string message = DacConnectionFactory.Explain(new Exception(Azure));

        Assert.DoesNotContain("already in use", message);
        Assert.Contains("Azure SQL Database does not offer one at all", message);
    }

    [Fact]
    public void WithoutAConnectionStringTheFailureIsNotBusy()
    {
        var ex = Assert.Throws<DacUnavailableException>(() => DacConnectionFactory.Open(null, "master"));

        Assert.False(ex.IsBusy);
    }
}
