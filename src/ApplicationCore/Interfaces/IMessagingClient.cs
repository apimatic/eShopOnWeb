using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider-owned state of a single message, as reported by the messaging
/// provider's API.
/// </summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    string? To,
    string? From,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

/// <summary>
/// Client for the messaging provider's message API (send, schedule, fetch,
/// cancel, redact, list). Implementations must never log destination numbers,
/// message bodies or credentials.
/// </summary>
public interface IMessagingClient
{
    Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-queued (scheduled) message that has not gone out yet.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task<ProviderMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's
    /// configured sending number within the given UTC date-time range. The
    /// provider is asked to filter by sender; no wider result set is filtered
    /// after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}
