using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessagingProvider
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken ct);
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken ct);
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);
    Task<ProviderMessage> CancelScheduledAsync(string providerSid, CancellationToken ct);
    Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken ct);
    Task<ProviderMessage> RedactBodyAsync(string providerSid, CancellationToken ct);
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken ct);
}

public sealed record PhoneLookupResult(
    bool IsUsable,
    string? CanonicalNumber,
    string? LineType,
    string? RejectionReason);

public sealed record ProviderMessage(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? Body,
    string? From,
    string? To,
    string? DateSent,
    string? DateCreated,
    string? MessagingServiceSid);
