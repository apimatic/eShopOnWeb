using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessagingProviderClient
{
    string ConfiguredFromNumber { get; }

    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken = default);

    Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? Body,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? To,
    string? From);
