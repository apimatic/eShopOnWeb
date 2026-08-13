using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>Outcome of validating/canonicalising a number with the provider.</summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalE164);

/// <summary>What the provider returned when a message was created (sent or scheduled).</summary>
public record SmsSendResult(string Sid, string Status, string? ErrorCode);

/// <summary>The provider's current view of a single message.</summary>
public record SmsStatusResult(string Sid, string Status, string? ErrorCode);

/// <summary>One message as the provider records it, used when reconciling.</summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

/// <summary>
/// Everything this integration needs from the SMS provider (Twilio), behind an abstraction so
/// the domain never depends on the transport. Implementations must never write a destination
/// number or the auth token to logs.
/// </summary>
public interface ISmsSender
{
    /// <summary>The application's own configured sending number, in E.164.</summary>
    string FromNumber { get; }

    /// <summary>
    /// Asks the provider whether a number is a usable destination and, if so, its canonical
    /// E.164 form. A number the provider does not consider usable comes back with IsValid=false.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>Queues a message with the provider to be sent at a future time.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current status for a message.</summary>
    Task<SmsStatusResult> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Calls off a scheduled message before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider (redaction), leaving the fact of the message
    /// and its outcome intact.
    /// </summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured number
    /// within a date range. The From-number filter is applied by the provider, not after the fact,
    /// and the whole range is covered (following pagination).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
