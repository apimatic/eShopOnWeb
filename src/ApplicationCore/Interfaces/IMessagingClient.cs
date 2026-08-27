using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A message as the messaging provider sees it. Field names mirror the provider's
/// message resource; <see cref="Status"/> holds the provider's delivery outcome.
/// </summary>
public record ProviderMessage(
    string Sid,
    string? From,
    string? To,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

/// <summary>
/// Sends, schedules, reads, cancels, redacts and lists SMS messages through the
/// messaging provider. Implementations must never log destination numbers or credentials.
/// </summary>
public interface IMessagingClient
{
    /// <summary>The application's own configured sending number.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Sends a message immediately, or queues it with the provider for <paramref name="sendAt"/>
    /// when supplied (provider-side scheduling, not an app-side timer).
    /// </summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-scheduled message that has not yet gone out.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of the message text at the provider so it is no longer retrievable there.</summary>
    Task<ProviderMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="from"/>
    /// whose sent date falls inside the given range. Covers the whole range (paged).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string from, DateTimeOffset? dateSentAfter, DateTimeOffset? dateSentBefore, CancellationToken cancellationToken = default);
}
