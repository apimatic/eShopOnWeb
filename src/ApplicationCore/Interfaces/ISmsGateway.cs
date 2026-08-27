using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level access to the SMS provider's messaging API. Implementations must never
/// log destination phone numbers or credentials.
/// </summary>
public interface ISmsGateway
{
    /// <summary>Send a message immediately.</summary>
    Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider for delivery at a future time.</summary>
    Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Cancel a not-yet-sent (scheduled) message at the provider.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message.</summary>
    Task<ProviderMessage?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured
    /// sending number within the given date-sent range. Covers the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Redact the body text of a message at the provider.</summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);
}

/// <summary>The provider's record of a single message.</summary>
public record ProviderMessage(
    string Sid,
    string? From,
    string? To,
    string? Body,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSentUtc,
    DateTimeOffset? DateCreatedUtc);
