using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record PhoneLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public sealed record ProviderMessageSnapshot(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? DateSent,
    string? DateCreated,
    string? From,
    string? To,
    string? MessagingServiceSid);

public interface ISmsNotificationGateway
{
    string ConfiguredFromNumber { get; }

    Task<PhoneLookupResult> LookupDestinationAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> SendImmediatelyAsync(string to, string body, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken);
}
