using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsNotificationGateway
{
    Task<SmsDispatchResult> SendImmediateAsync(string toE164, string body, CancellationToken cancellationToken);

    Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    string ConfiguredFromNumber { get; }

    Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record SmsMessageListResult(
    IReadOnlyList<SmsMessageSnapshot> Messages,
    bool Truncated);

public sealed record SmsDispatchResult(
    bool Succeeded,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

public sealed record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    string? Body,
    string? From,
    string? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
