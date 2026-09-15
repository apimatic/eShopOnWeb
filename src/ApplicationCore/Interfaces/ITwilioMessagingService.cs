using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider (Twilio) contract this integration depends on. The messaging capabilities
/// (send, schedule, fetch, cancel, redact, list) are served from the configured messaging
/// base address; number validation is served from the provider's separate Lookup host.
/// </summary>
public interface ITwilioMessagingService
{
    /// <summary>
    /// This application's own configured sending number (Twilio:FromNumber). Used to label the
    /// reconciliation report. This is the shop's number, not a shopper's.
    /// </summary>
    string ConfiguredFromNumber { get; }

    /// <summary>
    /// Validate a number with the provider and return its canonical E.164 form. A number the
    /// provider does not consider a usable destination comes back with <c>IsValid == false</c>.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Send a message now. Returns the provider's identifier and initial delivery status.</summary>
    Task<ProviderMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queue a message with the provider for a future send. The provider holds it and sends it
    /// at <paramref name="sendAt"/>; it is not held in this application.
    /// </summary>
    Task<ProviderMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Read the provider's current record of a message (status, error code, etc.).</summary>
    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>Call off a message that is still queued for a future send so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispose of a message's text at the provider (redact the body) while keeping the record
    /// that a message was sent and what became of it.
    /// </summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// The provider's own record of the messages it holds that were sent from this application's
    /// configured sending number within the given range. The provider is asked directly for that
    /// number's messages rather than filtering a wider answer after the fact.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

/// <summary>Result of validating a number with the provider.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalE164);

/// <summary>The provider's view of a single message.</summary>
public record ProviderMessage(
    string Sid,
    string Status,
    string? ErrorCode,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    string? Body);
