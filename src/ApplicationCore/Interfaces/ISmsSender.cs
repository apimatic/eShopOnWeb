using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The application's boundary over the SMS provider. Implementations translate provider transport,
/// error and deserialization faults into <see cref="Exceptions.SmsGatewayException"/> so the rest of
/// the app has a single failure type to handle, and never log message content or phone numbers.
/// </summary>
public interface ISmsSender
{
    /// <summary>Asks the provider whether a number is a usable destination and returns its canonical form.</summary>
    Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>Sends a message immediately.</summary>
    Task<SmsMessageResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAtUtc"/> (a few days later).</summary>
    Task<SmsMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken);

    /// <summary>Cancels a not-yet-sent scheduled message at the provider.</summary>
    Task<SmsMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Reads the provider's current record (and delivery outcome) for a message.</summary>
    Task<SmsMessageResult> GetStatusAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Disposes of a message's text at the provider (redaction). The message record itself survives.</summary>
    Task RedactContentAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the provider's own record of messages this application sent (filtered at the provider
    /// to this application's configured sending number) within a date range, for reconciliation.
    /// </summary>
    Task<ProviderMessagePage> ListSentMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int? page,
        string? pageToken,
        CancellationToken cancellationToken);
}

/// <summary>Result of a phone-number validation/lookup.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164, string? CountryCode);

/// <summary>Result of creating, reading, cancelling or scheduling a single message.</summary>
public record SmsMessageResult(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset SentAtUtc);

/// <summary>A message as the provider knows it, used for reconciliation.</summary>
public record ProviderMessage(string? Sid, string? Status, string? To, DateTimeOffset? DateSentUtc);

/// <summary>One page of provider messages plus the cursor to the next page, if any.</summary>
public record ProviderMessagePage(
    IReadOnlyList<ProviderMessage> Messages,
    int? NextPage,
    string? NextPageToken,
    bool HasMore);
