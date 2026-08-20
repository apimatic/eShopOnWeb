using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsLookupResult(bool IsUsable, string? CanonicalNumber, string? ErrorMessage);

public record SmsMessageSnapshot(
    bool Succeeded,
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? To,
    string? From,
    string? DateSent,
    string? DateCreated);

public interface ISmsGateway
{
    string SendingNumber { get; }

    Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> SendImmediateAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> FetchAsync(string sid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string sid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> RedactBodyAsync(string sid, CancellationToken cancellationToken);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAppAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
