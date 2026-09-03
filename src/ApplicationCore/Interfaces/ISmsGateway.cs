using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsSendResult(
    string? Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? To,
    string? DateSent);

public record SmsListItem(
    string? Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    string? DateSent,
    string? DateCreated);

public interface ISmsGateway
{
    string FromNumber { get; }

    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsSendResult> FetchAsync(string sid, CancellationToken cancellationToken);

    Task<SmsSendResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken);

    Task<SmsSendResult> RedactBodyAsync(string sid, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmsListItem>> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
