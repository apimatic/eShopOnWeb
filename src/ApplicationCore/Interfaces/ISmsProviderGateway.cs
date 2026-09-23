using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The shop's view of the SMS provider, in domain terms. The concrete implementation (Infrastructure) wraps
/// the Twilio SDK and is the only place provider types or the auth token exist. No method here leaks a
/// destination number or the token to logs.
/// </summary>
public interface ISmsProviderGateway
{
    /// <summary>
    /// Asks the provider whether a number is a usable destination and, if so, its canonical form.
    /// A definitive "not usable" answer is returned as <see cref="PhoneValidationResult.IsUsable"/> = false;
    /// only a non-definitive failure (provider unreachable, auth, throttling) throws
    /// <see cref="SmsProviderException"/>.
    /// </summary>
    Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken ct);

    /// <summary>Sends an SMS now, from the configured sending number. Throws on provider failure.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct);

    /// <summary>
    /// Queues an SMS with the provider to be sent at <paramref name="sendAt"/> (via the messaging service).
    /// The provider holds and sends it; nothing in this app runs a timer. Throws on provider failure.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct);

    /// <summary>Re-reads the provider's current state for a message. Throws on provider failure.</summary>
    Task<SmsStatusResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>Cancels a not-yet-sent (scheduled) message so it never goes out. Throws on provider failure.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Disposes of a message's content at the provider (redaction) so its text is no longer retrievable there,
    /// while the record that it was sent and its outcome survive. Throws on provider failure.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken ct);

    /// <summary>
    /// Asks the provider for one page of messages it sent FROM <paramref name="fromNumber"/> within the
    /// date range (filtered provider-side, not after the fact). Follow <see cref="ProviderMessagePage"/>
    /// pagination to cover the whole range. Throws on provider failure.
    /// </summary>
    Task<ProviderMessagePage> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to,
        int? page, string? pageToken, int pageSize, CancellationToken ct);
}

/// <summary>Definitive provider verdict on whether a number can be messaged, with its canonical form.</summary>
public record PhoneValidationResult(bool IsUsable, string? CanonicalNumber, string? Reason);

/// <summary>The provider-owned state returned when a message is created (immediately or scheduled).</summary>
public record SmsSendResult(string ProviderMessageSid, string ProviderStatusRaw, int? ErrorCode,
    string? ErrorMessage, DateTimeOffset? ProviderSentAt);

/// <summary>The provider-owned state returned when re-reading a message.</summary>
public record SmsStatusResult(string ProviderStatusRaw, int? ErrorCode, string? ErrorMessage,
    DateTimeOffset? ProviderSentAt);

/// <summary>One message as the provider records it, for reconciliation.</summary>
public record ProviderMessageSummary(string Sid, string? StatusRaw, string? To, string? From,
    DateTimeOffset? DateSent);

/// <summary>One page of provider messages, plus how to ask for the next page (null when there is none).</summary>
public record ProviderMessagePage(IReadOnlyList<ProviderMessageSummary> Messages, int? NextPage,
    string? NextPageToken);

/// <summary>
/// A provider call failed in a way the app cannot treat as an outcome (transport, auth, throttling, 5xx,
/// or an unreadable body). Carries a caller-safe message and, where known, the provider HTTP status.
/// Never carries the auth token or a destination number.
/// </summary>
public class SmsProviderException : Exception
{
    public int? StatusCode { get; }

    public SmsProviderException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
