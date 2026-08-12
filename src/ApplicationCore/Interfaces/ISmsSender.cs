using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>The provider's response to creating, scheduling, cancelling or fetching a message.</summary>
public record SmsSendResult(string? MessageSid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it, used for reconciliation.</summary>
public record ProviderMessageRecord(string MessageSid, string Status, string? From, DateTimeOffset? DateSent, int? ErrorCode);

/// <summary>
/// Abstraction over the provider's messaging API (Twilio). Every call honours the configured
/// messaging base URL. Implementations must never throw a phone number into a log.
/// </summary>
public interface ISmsSender
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber).</summary>
    string SendingNumber { get; }

    /// <summary>Sends a message now from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to go out at <paramref name="sendAt"/>. The provider holds
    /// it until due; it is not retained by this application.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a still-scheduled message so it never goes out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current record for a message (delivery outcome, error).</summary>
    Task<SmsSendResult> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the message body at the provider so its text is no longer retrievable there, while
    /// the record that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the configured sending number within a
    /// date range. The sending-number filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default);
}
