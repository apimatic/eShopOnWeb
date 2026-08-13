using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The boundary to the SMS provider (Twilio). Every provider interaction goes through this
/// interface; nothing above it knows how the provider is addressed or authenticated.
/// Implementations must never throw for a message that the provider merely could not deliver —
/// that is an outcome, reported on the returned result — and must never write a phone number to logs.
/// </summary>
public interface ISmsProvider
{
    /// <summary>The configured sending number (E.164). Reconciliation is scoped to messages sent from it.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Ask the provider whether a number is a usable destination and, if so, its canonical E.164 form.
    /// Used to reject an unusable number at registration rather than at send time.
    /// </summary>
    Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send an SMS now. A provider rejection is returned as a result, not thrown.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue an SMS with the provider to be sent at <paramref name="sendAt"/> (a few days out).
    /// The provider — not this application — holds it until then.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Call off a scheduled message with the provider before it is sent.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current record for a message (its live delivery outcome).</summary>
    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's text at the provider so the body is no longer retrievable there,
    /// while the message record and its delivery outcome survive.
    /// </summary>
    Task<bool> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of messages sent from the configured sending number within a date
    /// range. The provider is asked for that number's messages directly (server-side filter), rather
    /// than returning a wider answer to be filtered afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of validating a number with the provider.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Reason)
{
    public static PhoneNumberValidationResult Invalid(string reason) => new(false, null, reason);
    public static PhoneNumberValidationResult Valid(string canonical) => new(true, canonical, null);
}

/// <summary>Outcome of submitting (or scheduling) a message with the provider.</summary>
public record SmsSendResult(bool Accepted, string? MessageSid, string? Status, int? ErrorCode, string? ErrorMessage)
{
    public static SmsSendResult Failed(string reason) => new(false, null, null, null, reason);
}

/// <summary>A projection of the provider's Message resource that this integration cares about.</summary>
public record ProviderMessage(string Sid, string? Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent, string? To);
