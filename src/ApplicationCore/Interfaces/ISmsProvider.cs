using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outcome of validating/normalising a phone number with the provider.
/// </summary>
/// <param name="IsValid">Whether the provider considers the number a usable destination.</param>
/// <param name="E164PhoneNumber">The provider's canonical E.164 form (present when valid).</param>
/// <param name="NationalFormat">The provider's presentation format, for display.</param>
/// <param name="CountryCode">The provider's ISO country code for the number.</param>
/// <param name="ValidationErrors">Provider validation error codes when invalid.</param>
public record PhoneNumberValidation(
    bool IsValid,
    string? E164PhoneNumber,
    string? NationalFormat,
    string? CountryCode,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// The provider's view of a message it accepted or that we fetched back.
/// </summary>
/// <param name="Sid">The provider's message identifier.</param>
/// <param name="Status">The provider's current status (queued, sent, delivered, undelivered, ...).</param>
/// <param name="ErrorCode">Provider error code on failure/undelivered, if any.</param>
public record SmsSendResult(string Sid, string Status, int? ErrorCode);

/// <summary>
/// A message as it appears in the provider's own records, used for reconciliation.
/// </summary>
public record ProviderMessage(
    string Sid,
    string? From,
    string Status,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    bool HasBody);

/// <summary>
/// Everything this integration needs from the messaging provider. Implemented against the
/// provider's REST API. The messaging calls (send, fetch, cancel, redact, list) honour the
/// configured messaging base-URL override; number lookup is a separate host and does not.
/// Implementations must never write recipient numbers or message bodies to logs.
/// </summary>
public interface ISmsProvider
{
    /// <summary>
    /// Validates and normalises a number. Rejects a number the provider does not consider a usable
    /// destination and returns the provider's canonical E.164 form for one it accepts.
    /// </summary>
    Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken);

    /// <summary>Sends a message immediately from the configured sending number.</summary>
    Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken);

    /// <summary>Queues a message with the provider to be sent at <paramref name="sendAt"/>.</summary>
    Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    /// <summary>Reads the provider's current record for a message by its identifier.</summary>
    Task<SmsSendResult> FetchAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Cancels a scheduled message so it never goes out.</summary>
    Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>Redacts a message's body text at the provider so it can no longer be retrieved.</summary>
    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken);

    /// <summary>
    /// Lists the provider's own records of messages sent from the configured sending number within
    /// the given range. Asks the provider for that number's messages directly (not a wider answer
    /// filtered afterwards).
    /// </summary>
    Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
