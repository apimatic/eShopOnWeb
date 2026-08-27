using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioLookupResult(bool Valid, string? CanonicalPhoneNumber, IReadOnlyList<string>? ValidationErrors);

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public record TwilioSendResult(
    bool Succeeded,
    string? Sid,
    string Status,
    int? ErrorCode);

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<TwilioLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<TwilioSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioSendResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
