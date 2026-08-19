using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A gateway to the SMS messaging provider. It hides the provider's wire protocol behind
/// domain-friendly operations: validating/canonicalising a number, sending, scheduling,
/// cancelling, reading delivery state, redacting content, and listing sent messages for
/// reconciliation. Implementations must never write phone numbers or the auth secret to logs.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Asks the provider to validate a raw number and return its canonical E.164 form.
    /// Used at the edge, when a number is captured, so an unusable destination is rejected
    /// up front rather than at the moment a message fails to go out.
    /// </summary>
    Task<PhoneValidationResult> ValidateAndCanonicalizeAsync(string rawPhoneNumber, string? defaultCountryCode = null, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. The provider's acceptance is an acknowledgement, not a delivery receipt.</summary>
    Task<SentSms> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/> (a few days out).</summary>
    Task<SentSms> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a not-yet-sent (scheduled) message so it never reaches the shopper.</summary>
    Task<SentSms> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current authoritative state for one message.</summary>
    Task<SentSms?> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts the message body at the provider so its text is no longer retrievable there,
    /// while the record that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within a date range. The sender filter is applied by the provider, so
    /// traffic from other senders on the account is never returned.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of validating/canonicalising a number.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> ValidationErrors)
{
    public static PhoneValidationResult Valid(string canonicalE164) =>
        new(true, canonicalE164, Array.Empty<string>());

    public static PhoneValidationResult Invalid(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

/// <summary>A snapshot of a message the provider owns: its identifier and current delivery state.</summary>
public record SentSms(string Sid, string? Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent = null);

/// <summary>A message as returned by the provider's list endpoint, for reconciliation.</summary>
public record ProviderMessage(string Sid, string? To, string? From, string? Status, int? ErrorCode, DateTimeOffset? DateSent);
