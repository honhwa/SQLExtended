using SQLExtended.Decryption;
using Xunit;

namespace SQLExtended.Tests.Decryption;

/// <summary>
/// Tells "the DAC slot is occupied" apart from "the DAC is unusable", which is the distinction that decides
/// whether a failure is retried and whether it is remembered for the rest of the session.
///
/// Getting it wrong in one direction turns a permanent failure — a login that is not sysadmin — into an
/// endless retry that pays a connect timeout per object per cache refresh. Getting it wrong in the other
/// direction is the bug this was written for: one busy moment was cached as "no DAC on this server", so
/// decryption stayed dead until the user found "Clear All Cache", with an error message saying the server
/// was busy and no way to act on it.
///
/// <see cref="DacConnectionFactory.IsBusy"/> also matches the SQL error numbers, because the text is
/// localized by the server and a Chinese or German instance would never match on words. That half cannot be
/// tested here — <c>SqlException</c> has no public constructor — so it is pinned by the text half below.
/// </summary>
public class DacBusyClassificationTests
{
    [Theory]
    [InlineData("Could not connect because the maximum number of 'Dedicated Admin Connection' connections has already been reached.")]
    [InlineData("Could not connect because the maximum number of '1' user connections has already been reached.")]
    [InlineData("The dedicated administrator connection is already in use.")]
    public void BusyMessagesAreRecognised(string message)
    {
        Assert.True(DacConnectionFactory.LooksLikeBusyText(message));
    }

    [Theory]
    [InlineData("Login failed for user 'reporting'. Reason: The account is disabled.")]
    [InlineData("The text for object 'wsp_QueryReport' is encrypted.")]
    [InlineData("A network-related or instance-specific error occurred while establishing a connection to SQL Server.")]
    [InlineData("A network-related or instance-specific error occurred while establishing a connection to SQL Server. The server was not found or was not accessible. (provider: SQL Network Interfaces, error: 43 - An error occurred while obtaining the dedicated administrator connection (DAC) port. Make sure that SQL Browser is running, or check the error log for the port number)")]
    [InlineData("")]
    public void EverythingElseIsNotBusy(string message)
    {
        Assert.False(DacConnectionFactory.LooksLikeBusyText(message));
    }

    [Fact]
    public void NoMessageIsNotBusy()
    {
        Assert.False(DacConnectionFactory.LooksLikeBusyText(null));
    }

    /// <summary>
    /// The trap this classification has to avoid. <see cref="DacUnavailableException"/>'s own text names the
    /// dedicated administrator connection — the very phrase the busy check looks for — so an implementation
    /// that walked the whole exception chain would call every DAC failure busy, including "not sysadmin",
    /// and never cache a permanent answer. Only <c>SqlException</c> may be inspected.
    /// </summary>
    [Fact]
    public void TheWrappersOwnMessageIsNotEvidenceOfABusySlot()
    {
        var wrapped = new DacUnavailableException(
            "The server's single dedicated administrator connection is already in use, and it is the only way to read "
            + "encrypted module text. Close that session and try again.");

        Assert.False(DacConnectionFactory.IsBusy(wrapped));
    }

    [Fact]
    public void NoExceptionIsNotBusy()
    {
        Assert.False(DacConnectionFactory.IsBusy(null));
    }

    /// <summary>
    /// The same trap one level over: a refused login must be told apart from a transport failure, because
    /// only the first is worth remembering for the rest of the session. Its message names the sysadmin role,
    /// not a connection, so an implementation that matched on text would miss every localized instance —
    /// and would also be fooled by the wrapper, whose text mentions sysadmin too.
    /// </summary>
    [Fact]
    public void ARefusedLoginIsRecognisedThroughTheWrapperItArrivesIn()
    {
        var wrapped = new DacUnavailableException(
            "The administrator connection was refused: decrypting module text requires membership of the sysadmin server role.");

        Assert.False(DacConnectionFactory.IsLoginRefused(wrapped));
        Assert.False(DacConnectionFactory.IsLoginRefused(null));
    }
}
