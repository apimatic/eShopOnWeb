using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The app's abstraction over the SMS provider. Implemented in Infrastructure against the
/// Twilio SDK; ApplicationCore depends only on this interface and the plain DTOs below, so the
/// domain and orchestration stay provider-agnostic.
/// </summary>
public interface ISmsNotificationService
{
    /// <summary>The application's configured sending number (the <c>Twilio:FromNumber</c> value), for the reconciliation report.</summary>
    string ConfiguredFromNumber { get; }

    /// <summary>
    /// Asks the provider whether <paramref name="rawNumber"/> is a usable destination and, if so,
    /// returns the provider's own canonical (E.164) form. Used at registration time so an
    /// unusable number is rejected before any message is ever sent to it.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends an SMS immediately, from the application's configured sending number.</summary>
    Task<SentMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues an SMS with the provider to be sent at <paramref name="sendAt"/> (via the messaging service).</summary>
    Task<SentMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current view of a message by its identifier.</summary>
    Task<MessageDeliveryState> GetDeliveryStateAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a scheduled message with the provider before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Disposes of a message's body text at the provider so it can no longer be retrieved there.</summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending
    /// number within [<paramref name="from"/>, <paramref name="to"/>], covering the whole range.
    /// The From filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a number validation. <see cref="CanonicalNumber"/> is set only when <see cref="IsValid"/> is true.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>The provider's identifier and status for a message it has just accepted.</summary>
public record SentMessage(string ProviderMessageSid, string Status);

/// <summary>The provider's current delivery view of a message.</summary>
public record MessageDeliveryState(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One row of the provider's own message record, as returned by the reconciliation listing.</summary>
public record ProviderMessage(string? Sid, string? From, string? To, string? Status, DateTimeOffset? DateSent, string? Body);
