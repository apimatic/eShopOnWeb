using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A message as the messaging provider (Twilio) knows it. Field names and semantics
/// follow the provider's OpenAPI contract (api.v2010.account.message).
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
/// The shop's messaging provider. Implemented against the provider's OpenAPI
/// specification; sends, schedules, cancels, redacts and reconciles SMS messages.
/// </summary>
public interface IMessageProvider
{
    /// <summary>Send a message now, or queue it with the provider for <paramref name="scheduleAt"/>.</summary>
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message.</summary>
    Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message at the provider.</summary>
    Task<ProviderMessage?> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact the body text of a message at the provider; the message record itself survives.</summary>
    Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>List the provider's own record of messages sent from <paramref name="fromNumber"/> in a date range.</summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
