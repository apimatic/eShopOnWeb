using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneLookupResult(bool IsUsable, string? CanonicalNumber);

public record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? To,
    string? From,
    string? DateSent,
    string? DateCreated);

public record ProviderMessagePage(IReadOnlyList<ProviderMessage> Messages, string? NextPageUri);

public interface IMessagingProvider
{
    TimeSpan FollowUpDelay { get; }
    string FromNumber { get; }

    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken);
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessagePage> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        long? pageSize,
        int? page,
        string? pageToken,
        CancellationToken cancellationToken);
}
