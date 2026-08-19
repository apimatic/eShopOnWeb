using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Low-level access to the Twilio provider: number validation, sending/scheduling messages,
/// reading their delivery outcome, cancelling a scheduled message, disposing of a message body,
/// and listing messages for reconciliation. Implementations must never log phone numbers,
/// message bodies, or the auth token.
/// </summary>
public interface ITwilioMessagingClient
{
    /// <summary>
    /// Validates a number with the provider (Lookup) and returns its canonical E.164 form.
    /// A number the provider does not consider usable comes back with <c>Valid == false</c>.
    /// </summary>
    Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends a message now. Throws <see cref="TwilioApiException"/> on a provider error.</summary>
    Task<ProviderMessage> SendMessageAsync(string toE164, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a message to be sent at <paramref name="sendAt"/> via the messaging service.
    /// Throws <see cref="TwilioApiException"/> on a provider error.
    /// </summary>
    Task<ProviderMessage> ScheduleMessageAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Cancels a still-scheduled message so it never goes out.</summary>
    Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Fetches the provider's current view of a message (status, error code, ...).</summary>
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's body at the provider (redaction) so its text is no longer
    /// retrievable, while the message record and its outcome survive.
    /// </summary>
    Task<ProviderMessage> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from <paramref name="fromE164"/> within the
    /// day range covering [<paramref name="dateSentAfterUtc"/>, <paramref name="dateSentBeforeUtc"/>].
    /// The sender filter is applied by the provider, not after the fact. All pages are returned.
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromE164, DateTimeOffset dateSentAfterUtc,
        DateTimeOffset dateSentBeforeUtc, CancellationToken cancellationToken = default);
}

/// <summary>Result of a provider number validation.</summary>
public record PhoneLookupResult(bool Valid, string? PhoneNumberE164);

/// <summary>The subset of a Twilio message resource this integration relies on.</summary>
public record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? To,
    string? From,
    string? Body,
    DateTimeOffset? DateSent);
