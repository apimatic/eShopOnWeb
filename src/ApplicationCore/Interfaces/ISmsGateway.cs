using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record SmsLookupResult(bool IsUsable, string? CanonicalNumber);

public sealed record SmsSendResult(
    bool AcceptedByProvider,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

public sealed record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? DateCreated,
    string? DateSent);

public interface ISmsGateway
{
    Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<SmsSendResult> SendSmsAsync(string to, string body, CancellationToken cancellationToken);
    Task<SmsSendResult> ScheduleSmsAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}
