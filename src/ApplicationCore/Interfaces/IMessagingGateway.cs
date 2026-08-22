using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessagingGateway
{
    Task<ProviderMessage> SendAsync(OutboundMessageRequest request, CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> UpdateAsync(string providerMessageSid, MessageUpdateRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListFromSenderAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class OutboundMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public sealed class MessageUpdateRequest
{
    public string? Body { get; init; }
    public string? Status { get; init; }
}

public sealed class ProviderMessage
{
    public required string Sid { get; init; }
    public required string Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
}
