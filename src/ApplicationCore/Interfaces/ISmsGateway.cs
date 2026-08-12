using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's view of the SMS provider. It hides the provider SDK entirely: the rest of the app
/// speaks in canonical numbers, message SIDs and raw provider status strings, and never sees a provider type.
/// Every method throws <see cref="SmsGatewayException"/> on a provider or transport failure so callers have a
/// single failure type to guard.
/// </summary>
public interface ISmsGateway
{
    /// <summary>
    /// Ask the provider whether a typed-in number is a usable destination and, if so, its canonical form.
    /// A number the provider rejects must be turned away at registration, not at send time.
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct);

    /// <summary>Send a message now. Returns the provider's SID and the status it assigned on acceptance.</summary>
    Task<SmsSubmissionResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>
    /// Queue a message with the provider to go out at a future time. The provider holds it — nothing in this
    /// application waits to send it. Returns the SID so it can later be called off.
    /// </summary>
    Task<SmsSubmissionResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Call off a scheduled message before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Ask the provider for the current delivery outcome of a message.</summary>
    Task<SmsDeliveryState> FetchStatusAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Dispose of a message's content at the provider so its text can no longer be retrieved there, while the
    /// record of the message and what became of it survives.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// The provider's own record of messages sent from this application's configured sending number in a date
    /// range. The From-number filter is applied by the provider, so traffic from other numbers on the account
    /// is never returned.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Outcome of validating a typed-in number against the provider.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>What the provider returned when it accepted a message for sending or scheduling.</summary>
public record SmsSubmissionResult(string Sid, string? ProviderStatus, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current word on a message's delivery.</summary>
public record SmsDeliveryState(string? ProviderStatus, int? ErrorCode, string? ErrorMessage);

/// <summary>One row of the provider's own message log, used for reconciliation.</summary>
public record ProviderMessageRecord(
    string Sid, string? From, string? To, string? ProviderStatus, int? ErrorCode, DateTimeOffset? DateSent);
