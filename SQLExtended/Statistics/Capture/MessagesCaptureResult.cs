// -----------------------------------------------------------------------------
// Adapted from https://github.com/BrentOzarULTD/StatisticsParserExtension
//   commit e1526b4, file source/StatisticsParser.Vsix/Capture/MessagesCaptureResult.cs
// Copyright (c) 2026 Brent Ozar Unlimited. MIT License.
// See THIRD-PARTY-NOTICES.md.
// -----------------------------------------------------------------------------
using System;

namespace SQLExtended.Statistics.Capture;

/// <summary>Outcome of a Messages-pane capture attempt. Everything except <see cref="Ok"/> is a reason we have no text.</summary>
internal enum MessagesCaptureStatus
{
    Ok,
    NoActiveWindow,
    EmptyMessages,
    ContractsAssemblyMissing,
    ProxyUnavailable,
    Failed
}

/// <summary>
/// Result of <see cref="MessagesTabReader.GetMessagesTextAsync"/>: a status, the captured text when the status is
/// <see cref="MessagesCaptureStatus.Ok"/>, and the underlying exception for the failure statuses. Failures are
/// returned rather than thrown so the caller can show a status line instead of a stack trace.
/// </summary>
internal readonly struct MessagesCaptureResult
{
    public MessagesCaptureStatus Status { get; }
    public string Text { get; }
    public Exception Error { get; }

    private MessagesCaptureResult(MessagesCaptureStatus status, string text, Exception error)
    {
        Status = status;
        Text = text;
        Error = error;
    }

    public static MessagesCaptureResult Ok(string text) => new(MessagesCaptureStatus.Ok, text ?? string.Empty, null);

    public static MessagesCaptureResult NoActiveWindow() => new(MessagesCaptureStatus.NoActiveWindow, null, null);

    public static MessagesCaptureResult EmptyMessages() => new(MessagesCaptureStatus.EmptyMessages, string.Empty, null);

    public static MessagesCaptureResult ContractsAssemblyMissing(Exception error) => new(MessagesCaptureStatus.ContractsAssemblyMissing, null, error);

    public static MessagesCaptureResult ProxyUnavailable(Exception error) => new(MessagesCaptureStatus.ProxyUnavailable, null, error);

    public static MessagesCaptureResult Failed(Exception error) => new(MessagesCaptureStatus.Failed, null, error);
}
