using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsSendRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public record SmsSendResult(
    bool Succeeded,
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

public record SmsMessageSnapshot(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? DateSent,
    string? DateCreated);

public interface ISmsGateway
{
    string SendingNumber { get; }

    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsSendResult> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsSendResult> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
