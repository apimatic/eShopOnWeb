using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) as this application needs it. Implementations own the provider
/// contract, credentials and canonical sending number; the application core stays provider-neutral.
/// </summary>
public interface ISmsProvider
{
    /// <summary>The application's own configured sending number (Twilio:FromNumber), in E.164.</summary>
    string SendingNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical E.164 form.
    /// Used to reject unusable numbers at registration rather than at send time.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Throws <see cref="SmsProviderException"/> if the provider rejects the request.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for a future send. The provider holds and later sends it —
    /// this application does not. Throws <see cref="SmsProviderException"/> if the provider rejects it.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Calls off a message still scheduled with the provider, so it never sends.</summary>
    Task<ProviderMessageStatus> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Asks the provider for a message's current delivery outcome.</summary>
    Task<ProviderMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's body on the provider side so its text is no longer retrievable there,
    /// while the record that a message was sent — and what became of it — survives.
    /// </summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <see cref="SendingNumber"/> within the
    /// date range. The provider is asked for that number's messages directly (server-side filter),
    /// so other traffic on the account is never pulled back and filtered away afterwards.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a carrier lookup used to validate a number before it is stored.</summary>
public sealed record PhoneLookupResult(bool IsValid, string? CanonicalE164, string? Reason);

/// <summary>What the provider returned when a message was accepted for delivery or scheduling.</summary>
public sealed record SmsSendResult(string MessageSid, string? Status, int? ErrorCode);

/// <summary>The provider's current view of a message's delivery outcome.</summary>
public sealed record ProviderMessageStatus(string? Status, int? ErrorCode);

/// <summary>One row of the provider's own message log, used for reconciliation.</summary>
public sealed record ProviderMessageRecord(string Sid, string? Status, string? From, string? To, DateTimeOffset? DateSent, int? ErrorCode);

/// <summary>Raised when the provider rejects a request. Never carries the destination number or body.</summary>
public class SmsProviderException : Exception
{
    public int? ProviderErrorCode { get; }
    public string? ProviderStatus { get; }

    public SmsProviderException(string message, int? providerErrorCode = null, string? providerStatus = null)
        : base(message)
    {
        ProviderErrorCode = providerErrorCode;
        ProviderStatus = providerStatus;
    }
}
