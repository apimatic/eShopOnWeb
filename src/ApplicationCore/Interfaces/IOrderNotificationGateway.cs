using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the SMS provider (Twilio) used for order notifications. Implementations build
/// strictly against the provider's OpenAPI contract. The provider's own configured sending number
/// and secrets live behind this abstraction, so callers never handle them.
/// </summary>
public interface IOrderNotificationGateway
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical form.
    /// Used to reject unusable numbers at registration time rather than at send time.
    /// </summary>
    Task<PhoneValidationResult> ValidateDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message immediately from the configured sender. Throws <see cref="NotificationGatewayException"/> if the provider cannot be reached.</summary>
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue a message with the provider to be sent at <paramref name="sendAt"/> (a few days out).
    /// The provider — not this application — holds it until then.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Call off a not-yet-sent scheduled message so it never reaches the shopper.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetch the provider's current record of a message (its live delivery outcome).</summary>
    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's text content at the provider (redaction), leaving the fact it was sent
    /// and what became of it intact.
    /// </summary>
    Task DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of the messages it sent from the configured sending number within a
    /// date range — asked of the provider directly, filtered by that sender, for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentByConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a destination-number validation.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>The provider's view of a single message, mapped from the provider's Message resource.</summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? Body);
