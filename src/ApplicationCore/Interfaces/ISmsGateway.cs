using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsSendRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public record SmsMessageSnapshot(
    string Sid,
    string Status,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public interface ISmsGateway
{
    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
