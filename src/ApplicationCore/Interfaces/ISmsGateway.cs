using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of asking the provider to accept a message (immediate or scheduled).</summary>
public record SmsSendResult(string Sid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A snapshot of the provider's own record of a single message.</summary>
public record SmsMessageState(
    string Sid,
    string Status,
    string? To,
    string? From,
    string? Body,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Abstraction over the messaging provider's message API (send, read, cancel, redact, list).
/// The implementation is the only thing that speaks to the provider; it is built to the
/// provider's OpenAPI contract. Application code never fails an order operation because a
/// send failed — send methods surface provider rejection to the caller to record, not to
/// abort the business action.
/// </summary>
public interface ISmsGateway
{
    /// <summary>The application's configured sending number (Twilio:FromNumber), in E.164 form.</summary>
    string SenderNumber { get; }

    /// <summary>Sends a message now, from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for delivery at <paramref name="sendAt"/>. The wait
    /// is held by the provider, not by this application.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record for a message, or null if it is unknown.</summary>
    Task<SmsMessageState?> FetchAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is scheduled but has not yet gone out.</summary>
    Task CancelScheduledAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message text at the provider so it can no longer be retrieved there.</summary>
    Task RedactBodyAsync(string sid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromNumber"/>
    /// over a date range, walking every page. The provider is asked to filter by sender, so
    /// traffic from other numbers on the account is never returned.
    /// </summary>
    Task<IReadOnlyList<SmsMessageState>> ListSentFromAsync(
        string fromNumber, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
