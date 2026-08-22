using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsSendCommand(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public record SmsMessageSnapshot(
    string Sid,
    string Status,
    int? ErrorCode,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public record SmsSendResult(
    bool Accepted,
    SmsMessageSnapshot? Message,
    int? ErrorCode);

public interface ISmsGateway
{
    string FromNumber { get; }
    Task<SmsSendResult> SendAsync(SmsSendCommand command, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
