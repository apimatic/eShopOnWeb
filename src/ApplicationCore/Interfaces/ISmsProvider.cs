using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the messaging provider (Twilio) that this integration sends,
/// reads, schedules, cancels, redacts and reconciles messages through. Kept in the
/// core so orchestration does not depend on any provider SDK or transport detail.
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's configured sending number (E.164), used for reconciliation reporting.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its
    /// canonical E.164 form. Used to accept/reject a number at registration time.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Returns the provider's message id and initial status.</summary>
    Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAtUtc"/>.
    /// The provider — not this application — holds it until then.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current view of a single message.</summary>
    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a still-scheduled message with the provider so it never goes out.</summary>
    Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable
    /// there, while the message record (id and delivery outcome) survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's
    /// configured sending number within the given range, for reconciliation. The
    /// filter by sending number is applied at the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a provider phone-number lookup.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber, string? LineType);

/// <summary>The provider's view of a message: its identifier and delivery state.</summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? To,
    string? From,
    string? Body,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);
