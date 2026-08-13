using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Provider-neutral view of the SMS messaging provider (Twilio). The concrete implementation
/// lives in Infrastructure and holds the provider configuration (sending number, messaging
/// service, base URL). Everything the application does with the provider goes through here so
/// the domain never depends on the provider SDK.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical
    /// E.164 form. This is the registration-time gate — a number the provider rejects here is
    /// never stored, so a later message can never target it.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now. Never throws for a provider/carrier outcome — failures come back on the result.</summary>
    Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to be sent at a future time.</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default);

    /// <summary>Call off a message that was scheduled but has not yet gone out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current record of a message (its delivery outcome).</summary>
    Task<SmsDeliveryState?> FetchStateAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose the content of a message at the provider so its text is no longer retrievable,
    /// while the message record (identifier, status) survives.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured sending
    /// number within a date range. The From filter is applied by the provider, not here.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListOutboundAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a registration-time number validation.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>Outcome of a send/schedule attempt.</summary>
public record SmsDispatchResult(string? ProviderMessageSid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A later read of the provider's delivery outcome for a message.</summary>
public record SmsDeliveryState(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it, for reconciliation.</summary>
public record ProviderMessageRecord(string Sid, string? Status, string? To, string? From, DateTimeOffset? DateSent, int? ErrorCode);
