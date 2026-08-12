using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's contract to the SMS provider (Twilio). The implementation is built
/// against the Twilio OpenAPI specification. The provider owns the account's sending number and
/// messaging service, so callers never pass a "from" — sending, scheduling and reconciliation all
/// use the account's own configured sender internally.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Validates and canonicalizes a phone number with the provider before it is put on file.
    /// Returns the provider's own canonical E.164 form and whether it is a usable destination.
    /// </summary>
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    /// <summary>Sends an SMS immediately from the account's configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues an SMS with the provider to be sent at <paramref name="sendAt"/> (a future time).
    /// The message is held by the provider, not by this application.
    /// </summary>
    Task<SmsSendResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    /// <summary>Reads the provider's current record (including delivery status) for a message.</summary>
    Task<SmsSendResult> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes of a message's text at the provider (redaction) so it is no longer retrievable there,
    /// while the fact the message was sent and what became of it survives.
    /// </summary>
    Task<SmsSendResult> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>Cancels a not-yet-sent scheduled message at the provider so it never goes out.</summary>
    Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the provider's own record of messages sent from THIS application's configured sending
    /// number within a date range. The sending-number filter is applied by the provider (asked for
    /// directly), not by filtering a wider answer after the fact.
    /// </summary>
    Task<IReadOnlyList<SmsSendResult>> ListOwnMessagesAsync(DateTimeOffset dateSentFrom, DateTimeOffset dateSentTo, CancellationToken cancellationToken = default);
}

/// <summary>The provider's verdict on a phone number.</summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalPhoneNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>A projection of the provider's message resource that the app persists and reports on.</summary>
public record SmsSendResult(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
