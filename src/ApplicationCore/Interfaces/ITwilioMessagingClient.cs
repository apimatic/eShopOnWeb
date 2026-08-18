using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A minimal, provider-owned view of a single message resource, as the messaging API reports it.
/// </summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSentUtc,
    string? Body);

/// <summary>
/// Thin abstraction over the provider's messaging API (send, schedule, fetch, cancel, redact,
/// list). Every method talks to the messaging host only, honouring the optional base-URL override.
/// Implementations translate provider failures into <see cref="Exceptions.MessagingProviderException"/>.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>Sends a message immediately from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAtUtc"/>.</summary>
    Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Reads a message resource back so its current delivery outcome can be observed.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts a message's body text at the provider so it is no longer retrievable there.</summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending
    /// number within the given range. The sender filter is applied by asking the provider for that
    /// number's messages, not by filtering a wider answer afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
