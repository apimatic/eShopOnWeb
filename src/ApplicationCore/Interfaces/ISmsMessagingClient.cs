using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Provider-owned state for a single message.</summary>
public record SmsMessageResult(
    string ProviderMessageSid,
    string Status,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

/// <summary>
/// Low-level messaging provider client. All methods talk to the provider's messaging API;
/// none of them ever log destination numbers or message bodies.
/// </summary>
public interface ISmsMessagingClient
{
    /// <summary>
    /// Sends a message immediately, or queues it with the provider for <paramref name="sendAtUtc"/>
    /// when supplied (provider-side scheduling, requires a messaging service).
    /// </summary>
    Task<SmsMessageResult> SendMessageAsync(string toNumber, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default);

    Task<SmsMessageResult> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a provider-scheduled message that has not gone out yet.</summary>
    Task<SmsMessageResult> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redacts the message body at the provider so the text is no longer retrievable there.</summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number whose sent date falls in [from, to] (UTC). The sender filter is applied
    /// provider-side. Covers the whole range, following pagination.
    /// </summary>
    Task<IReadOnlyList<SmsMessageResult>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
