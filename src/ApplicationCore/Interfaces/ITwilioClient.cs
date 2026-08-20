using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioLookupResult(bool Valid, string? CanonicalPhoneNumber, IReadOnlyList<string> ValidationErrors);

public record TwilioMessageResult(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? DateSent,
    string? DateCreated,
    string? From);

public interface ITwilioLookupClient
{
    Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<TwilioMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageResult>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
