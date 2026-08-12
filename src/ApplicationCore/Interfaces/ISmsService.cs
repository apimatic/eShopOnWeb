using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Application-facing port over the messaging provider. The implementation lives in Infrastructure and is
/// the only thing that knows about the concrete provider (Twilio). Every provider failure surfaces as
/// <see cref="Exceptions.SmsProviderException"/>; no method here ever logs the auth token or a number.
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.
    /// A determinable "not usable" answer comes back as <c>IsValid == false</c>; a failure to reach the
    /// provider throws <see cref="Exceptions.SmsProviderException"/>.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send an SMS immediately from the application's configured sending number.</summary>
    Task<SentSmsMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (held by the provider, not this app).</summary>
    Task<SentSmsMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancel a previously scheduled message at the provider before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current delivery outcome for a single message.</summary>
    Task<SmsMessageState> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redact the message body at the provider so its text is no longer retrievable there, while the
    /// record of the message (its SID and status) survives.
    /// </summary>
    Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of every message sent from the application's configured sending number
    /// within the inclusive date range, paged through in full. Used for reconciliation.
    /// </summary>
    Task<IReadOnlyList<ProviderSmsRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a provider phone-number validation.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>A message the provider has accepted, with its identifier and initial status.</summary>
public record SentSmsMessage(string Sid, string Status);

/// <summary>A message's current provider delivery state.</summary>
public record SmsMessageState(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One message as the provider records it (for reconciliation).</summary>
public record ProviderSmsRecord(string Sid, string? Status, string? To, string? From, DateTimeOffset? DateSent, string? Body);
