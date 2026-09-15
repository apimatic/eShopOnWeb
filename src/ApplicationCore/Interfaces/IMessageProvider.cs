using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessageProvider
{
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string providerMessageId,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string providerMessageId,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactAsync(string providerMessageId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderMessage(string Id, string Status, string? From, string? To,
    string? Body, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, int? ErrorCode,
    string? ErrorMessage);
