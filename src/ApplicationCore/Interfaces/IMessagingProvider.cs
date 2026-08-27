using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A message as the messaging provider sees it. Field names/semantics follow the
/// provider's OpenAPI contract (api-specs/twilio).
/// </summary>
public record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    string? From,
    string? To);

/// <summary>
/// The messaging provider (Twilio). Implementations are built against the OpenAPI
/// specifications in api-specs/ — no pre-built provider SDK.
/// </summary>
public interface IMessagingProvider
{
    /// <summary>
    /// Sends a message immediately, or schedules it with the provider when
    /// <paramref name="scheduleAt"/> is supplied (provider-side scheduling, not an app timer).
    /// </summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default);

    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages in [from, to], restricted at the
    /// source to the application's configured sending number.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message at the provider.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
