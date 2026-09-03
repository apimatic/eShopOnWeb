using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public sealed record ProviderMessageSnapshot(
    string? Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateCreated,
    string? DateSent);

public sealed record SendMessageRequest(
    string To,
    string Body,
    bool ScheduleForLater,
    DateTimeOffset? SendAt);

public sealed record ProviderMessageList(IReadOnlyList<ProviderMessageSnapshot> Messages, bool Truncated);

public interface IMessagingGateway
{
    string ConfiguredFromNumber { get; }

    Task<PhoneLookupResult> LookupNumberAsync(string phoneNumber, CancellationToken ct);

    Task<ProviderMessageSnapshot> SendAsync(SendMessageRequest request, CancellationToken ct);

    Task<ProviderMessageSnapshot?> FetchAsync(string providerSid, CancellationToken ct);

    Task<ProviderMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken ct);

    Task<ProviderMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken ct);

    Task<ProviderMessageList> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct);
}
