using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record SmsLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public sealed record ProviderMessageResult(
    string? Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    string? DateSent,
    string? DateCreated,
    int? ErrorCode,
    string? ErrorMessage);

public interface ISmsNotificationGateway
{
    Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken ct);
    Task<ProviderMessageResult> SendAsync(string to, string body, CancellationToken ct);
    Task<ProviderMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);
    Task<ProviderMessageResult> CancelScheduledAsync(string sid, CancellationToken ct);
    Task<ProviderMessageResult> FetchAsync(string sid, CancellationToken ct);
    Task<ProviderMessageResult> RedactBodyAsync(string sid, CancellationToken ct);
    Task<ProviderMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken ct);
}

public sealed record ProviderMessageListResult(IReadOnlyList<ProviderMessageResult> Messages, bool Truncated);
