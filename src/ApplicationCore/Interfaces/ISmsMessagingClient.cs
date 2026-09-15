using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SendSmsRequest(string To, string Body, DateTimeOffset? SendAt = null);

public record SmsMessageResult(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? Uri);

public interface ISmsMessagingClient
{
    string FromNumber { get; }

    Task<SmsMessageResult> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
