using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? Body,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? From);

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<ProviderMessage> SendAsync(
        string toE164,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
