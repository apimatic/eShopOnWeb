using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The messaging provider (Twilio) abstraction. Every Twilio interaction the application performs goes
/// through this seam. Implementations own the wire details (hosts, auth, encoding) and must never log
/// phone numbers, message bodies, or credentials.
/// </summary>
public interface ISmsProvider
{
    /// <summary>This application's own configured sending number (E.164), used for reconciliation.</summary>
    string ConfiguredSenderNumber { get; }

    /// <summary>
    /// Validate a raw caller-supplied number and return the provider's canonical form. A number the
    /// provider does not consider a usable destination comes back with <c>IsValid == false</c>.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now from this application's configured sending number.</summary>
    Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>Queue a message with the provider to go out at <paramref name="sendAt"/> (a future time).</summary>
    Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current authoritative state for a message, or null if unknown.</summary>
    Task<SmsMessageState?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancel a scheduled message before it goes out.</summary>
    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Redact the message body at the provider so its content is no longer retrievable there.</summary>
    Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// List the provider's own record of messages sent from this application's configured sending
    /// number within the date range. Covers the whole range (all pages).
    /// </summary>
    Task<IReadOnlyList<SmsMessageState>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating and canonicalizing a phone number.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>Result of a create/send call: the provider's identifier and the initial delivery outcome.</summary>
public record SmsDispatchResult(string Sid, string Status, int? ErrorCode);

/// <summary>A snapshot of the provider's state for one message.</summary>
public record SmsMessageState(string Sid, string Status, int? ErrorCode, string? To, string? From, DateTimeOffset? DateSent);
