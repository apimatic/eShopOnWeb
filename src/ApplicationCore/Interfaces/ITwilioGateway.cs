using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool Valid, string? CanonicalPhoneNumber);

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public record SendSmsRequest(string To, string Body, DateTimeOffset? SendAt);

public interface ITwilioGateway
{
    Task<PhoneLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> SendSmsAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> UpdateMessageAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
