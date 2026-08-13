using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Abstraction over the messaging provider (Twilio). Everything eShop needs to know about a
/// message has to be obtained by asking the provider — there is no callback URL — so this surface
/// covers validating a destination, sending now, scheduling for later, reading current state,
/// cancelling a not-yet-sent message, redacting content, and listing the provider's own records.
/// </summary>
public interface ISmsSender
{
    /// <summary>The application's configured sending number (Twilio:FromNumber), in E.164.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and returns its canonical (E.164)
    /// form. Used to reject un-sendable numbers at registration time rather than at send time.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now, from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a message with the provider for future delivery. The provider holds and sends it; this
    /// application does not keep any timer of its own.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record for a message (its status and metadata).</summary>
    Task<SmsSendResult> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a message that is still scheduled and has not yet gone out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Redacts a message's body at the provider so its text is no longer retrievable there, while the
    /// record of the message (and what became of it) survives.
    /// </summary>
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own records of messages sent from <paramref name="fromNumber"/> within the
    /// range. The provider is asked for that number's messages directly, not a wider answer filtered
    /// after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a number validation / canonicalisation against the provider.</summary>
public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }

    /// <summary>The provider's canonical E.164 form of the number, when valid.</summary>
    public string? CanonicalNumber { get; init; }

    public string? ValidationError { get; init; }
}

/// <summary>The provider's response to creating, scheduling or reading a message.</summary>
public class SmsSendResult
{
    public string Sid { get; init; } = string.Empty;
    public string Status { get; init; } = SmsDeliveryStatus.Unknown;
    public DateTimeOffset? DateSent { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>A single message as the provider records it, used when reconciling.</summary>
public class ProviderMessageRecord
{
    public string Sid { get; init; } = string.Empty;
    public string Status { get; init; } = SmsDeliveryStatus.Unknown;
    public string? From { get; init; }
    public string? To { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public int? ErrorCode { get; init; }
}
