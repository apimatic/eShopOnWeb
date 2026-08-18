using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A provider-agnostic gateway to the SMS provider (Twilio). Everything about the concrete SDK is confined to
/// the Infrastructure implementation; the domain talks only in these neutral shapes. Any provider failure is
/// surfaced as an <see cref="Exceptions.SmsProviderException"/> so callers have a single failure type to handle.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Validate and canonicalize a destination number at registration time. The returned
    /// <see cref="PhoneValidationResult.CanonicalNumber"/> is the provider's own E.164 form, to be stored in
    /// place of whatever the caller typed.</summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken);

    /// <summary>Send an SMS immediately from the application's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (via the messaging service).
    /// The message is held by the provider, not by this application.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Cancel a not-yet-sent scheduled message at the provider so it never reaches the customer.</summary>
    Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    /// <summary>Read the current delivery outcome of a single message from the provider.</summary>
    Task<SmsStatusResult> GetStatusAsync(string providerSid, CancellationToken cancellationToken);

    /// <summary>Dispose of a message's content at the provider (blank the body) while keeping the message record
    /// and its outcome intact.</summary>
    Task RedactContentAsync(string providerSid, CancellationToken cancellationToken);

    /// <summary>List the provider's own record of messages sent from the application's configured sending
    /// number within the inclusive date-time range, for reconciliation. The provider is asked for that number's
    /// messages directly (a from-number filter on the query), not by filtering a wider answer after the fact.</summary>
    Task<IReadOnlyList<ProviderMessage>> ListOwnMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>Outcome of validating a destination number.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> Reasons);

/// <summary>Outcome of a send/schedule call: the provider's message identifier and the status it reported.</summary>
public record SmsSendResult(string Sid, string? Status);

/// <summary>A single message's delivery outcome as read back from the provider.</summary>
public record SmsStatusResult(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it, used by reconciliation.</summary>
public record ProviderMessage(string? Sid, string? Status, string? To, string? From, string? DateSent);
