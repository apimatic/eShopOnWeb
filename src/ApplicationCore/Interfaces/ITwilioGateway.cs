using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioLookupResult(bool Valid, string? CanonicalPhoneNumber, IReadOnlyList<string> ValidationErrors);

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? From);

public interface ITwilioGateway
{
    string ConfiguredFromNumber { get; }

    Task<TwilioLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
