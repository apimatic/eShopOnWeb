using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(bool Valid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    int? ErrorCode,
    string? ErrorMessage);

public record SendSmsRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public interface ITwilioApiClient
{
    /// <summary>
    /// The configured sending number (<c>Twilio:FromNumber</c>). Used when asking
    /// the provider to list this application's messages.
    /// </summary>
    string ConfiguredFromNumber { get; }

    /// <summary>
    /// Basic Lookup on lookups.twilio.com — not governed by Twilio:BaseUrl.
    /// </summary>
    Task<PhoneNumberLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> SendMessageAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists messages Twilio sent from <paramref name="fromNumber"/> in the given range.
    /// The From filter is applied by the provider, not after the fact.
    /// </summary>
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
