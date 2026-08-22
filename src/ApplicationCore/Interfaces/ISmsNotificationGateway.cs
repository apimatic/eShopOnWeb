using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool IsUsable, string? CanonicalNumber, string? RejectionReason);

public record ProviderMessageSnapshot(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    string? DateCreated,
    string? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

public interface ISmsNotificationGateway
{
    Task<PhoneLookupResult> LookupDestinationAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<ProviderMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ProviderMessageSnapshot> Messages, bool Truncated)> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken);
}
