using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneNumberLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public sealed record SmsDispatchResult(
    bool ReachedProvider,
    string? ProviderSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage);

public sealed record SmsMessageSnapshot(
    string Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? To,
    string? From,
    string? DateSent,
    string? DateCreated);

public sealed record SmsListPage(
    IReadOnlyList<SmsMessageSnapshot> Messages,
    string? NextPageToken,
    bool HasMore);

public interface ISmsProvider
{
    Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken);
    Task<SmsDispatchResult> SendImmediateAsync(string toCanonical, string body, CancellationToken cancellationToken);
    Task<SmsDispatchResult> ScheduleAsync(string toCanonical, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken);
    Task<SmsDispatchResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<SmsListPage> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? pageToken,
        CancellationToken cancellationToken);
}
