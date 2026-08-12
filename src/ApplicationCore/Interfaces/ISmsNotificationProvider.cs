using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) as the domain sees it: a thin, SDK-free seam over the handful of
/// provider capabilities the notification flows need. The implementation lives in Infrastructure.
/// Because there is no public callback URL for this app, everything about a message's fate has to be
/// obtained by asking the provider — hence <see cref="FetchStateAsync"/> and <see cref="ListSentMessagesAsync"/>.
/// </summary>
public interface ISmsNotificationProvider
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical E.164 form.
    /// Used at registration time so an unusable number is rejected before any message is ever attempted.
    /// Throws only if the provider itself could not be reached (a validation "no" is a normal result).
    /// </summary>
    Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default);

    /// <summary>Sends a message now. Returns the provider's identifier and initial status. Throws on provider failure.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default);

    /// <summary>
    /// Queues a message with the provider to be sent at <paramref name="sendAt"/>. The provider — not this
    /// app — holds it until then. Returns the provider identifier and initial status. Throws on provider failure.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default);

    /// <summary>Calls off a scheduled message with the provider before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>Reads the provider's current record of a message: its delivery status and any error.</summary>
    Task<SmsMessageState> FetchStateAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Disposes of a message's text at the provider so it is no longer retrievable there, while the record
    /// that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured sending number
    /// within a date range, paging across the whole range. Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);
}

/// <summary>Outcome of a number validation: whether it is usable and the provider's canonical form.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>The provider's identifier for a just-created message and the status it reported.</summary>
public record SmsSendResult(string ProviderMessageSid, string? Status);

/// <summary>A message's current provider-owned state.</summary>
public record SmsMessageState(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One row of the provider's own message record, as returned for reconciliation.</summary>
public record ProviderMessageRecord(string ProviderMessageSid, string? To, string? From, string? Status, DateTimeOffset? DateSent, string? Body);
