using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default);
    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListByFromNumberAsync(DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
}

public sealed class SendProviderMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public sealed class ProviderMessage
{
    public required string Sid { get; init; }
    public required string Status { get; init; }
    public int? ErrorCode { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public string? Body { get; init; }
    public string? Direction { get; init; }
}
