using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Sends, reads, schedules, cancels, redacts and reconciles SMS messages through the messaging
/// provider. This is the single seam between the application and the provider's messaging API;
/// implementations own all provider-specific concerns (endpoints, auth, the configured sending
/// number). Methods that talk to the provider throw <see cref="SmsGatewayException"/> on failure —
/// callers decide whether that should affect the underlying operation.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SmsMessageState> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a future time. The provider holds
    /// and sends it — the application does not run a timer of its own.</summary>
    Task<SmsMessageState> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record of a message by its identifier.</summary>
    Task<SmsMessageState> FetchAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message the provider has not yet sent (a scheduled follow-up).</summary>
    Task<SmsMessageState> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts a message's text content at the provider so it is no longer retrievable there,
    /// while the record that the message was sent, and its outcome, survives.</summary>
    Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);

    /// <summary>Lists the provider's own record of messages sent from the application's configured
    /// sending number within the given date range, covering the whole range (following provider paging).</summary>
    Task<IReadOnlyList<SmsMessageState>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>
/// A provider message as the provider currently sees it.
/// </summary>
/// <param name="Sid">The provider's identifier for the message.</param>
/// <param name="Status">The provider's current delivery status.</param>
/// <param name="ErrorCode">Provider error code when the message failed/was undelivered.</param>
/// <param name="ErrorMessage">Provider error description when the message failed/was undelivered.</param>
/// <param name="To">Destination number as the provider recorded it.</param>
/// <param name="From">Sending number as the provider recorded it.</param>
/// <param name="SentAt">Provider timestamp for when the message was sent, when known.</param>
public record SmsMessageState(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    DateTimeOffset? SentAt);

/// <summary>Raised when a call to the messaging provider fails.</summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message) : base(message) { }
    public SmsGatewayException(string message, Exception inner) : base(message, inner) { }
}
