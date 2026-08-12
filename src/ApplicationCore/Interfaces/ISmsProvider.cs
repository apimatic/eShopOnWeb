using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The result of asking the provider to validate a phone number.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> Errors);

/// <summary>
/// A message resource as the provider reports it: its identifier plus the delivery state the
/// provider owns. Used both for sends and for reads/reconciliation.
/// </summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

/// <summary>
/// Abstraction over the SMS provider's messaging and lookup surface. The concrete implementation is
/// the only place that speaks the provider's wire protocol; everything above it works in these terms.
/// </summary>
public interface ISmsProvider
{
    /// <summary>The application's own configured sending number (E.164), used as the message sender and as the reconciliation filter.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Validates a number against the provider and returns its canonical E.164 form. A number the
    /// provider does not consider a usable destination comes back with <c>IsValid == false</c>.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now, from the configured sending number.</summary>
    Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/> (not held in this application).</summary>
    Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's authoritative current state for one message.</summary>
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the message body at the provider so its text is no longer retrievable there, while the
    /// record of the message and its outcome survives.
    /// </summary>
    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's record of messages sent from the configured sending number within the given
    /// range. The provider is asked for that number's messages directly (a sender-side filter), not a
    /// wider answer filtered afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
