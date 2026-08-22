using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public interface ISmsGateway
{
    Task<SmsDispatchResult> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageSnapshot> RedactContentAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsListResult> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record SmsDispatchResult(
    bool Accepted,
    string? ProviderSid,
    string? Status,
    string? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

public sealed record SmsMessageSnapshot(
    bool Succeeded,
    string? Sid,
    string? Status,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent,
    string? DateCreated,
    string? FailureMessage);

public sealed record SmsListResult(
    bool Succeeded,
    IReadOnlyList<SmsMessageSnapshot> Messages,
    bool Truncated,
    string? FailureMessage);
