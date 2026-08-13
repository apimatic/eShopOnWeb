using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Port onto the SMS messaging provider (Twilio). The application core depends only on this
/// abstraction; the concrete provider integration lives in Infrastructure. All destination
/// numbers and message bodies passed here are considered sensitive and must never be logged.
/// </summary>
public interface INotificationGateway
{
    /// <summary>
    /// Asks the provider whether a raw, caller-supplied number is a usable destination and returns
    /// the provider's canonical E.164 form. A number the provider does not consider usable comes back
    /// with <see cref="PhoneValidationResult.IsValid"/> = false.
    /// </summary>
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message immediately from the application's configured sending number.</summary>
    Task<SentMessageResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SentMessageResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message the provider has accepted but not yet sent.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current view of a message. Null if the message is unknown to the provider.</summary>
    Task<MessageDeliveryState?> FetchStateAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's content at the provider so its text is no longer retrievable, while the
    /// message record and its delivery outcome survive.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from the application's configured sending
    /// number within the given range. The sending-number filter is applied by the provider, not here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating a phone number against the provider.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>Outcome of a send/schedule attempt that reached the provider.</summary>
public record SentMessageResult(string Sid, string Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? SentAt);

/// <summary>The provider's current view of a message's delivery.</summary>
public record MessageDeliveryState(string Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? SentAt);

/// <summary>One row of the provider's own message ledger, used for reconciliation.</summary>
public record ProviderMessageRecord(string Sid, string? To, string? From, string Status, DateTimeOffset? DateSent, int? ErrorCode);
