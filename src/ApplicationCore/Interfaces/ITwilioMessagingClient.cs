using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ProviderMessage(
    string Sid,
    string? Status,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent,
    string? DateCreated);

public record SendProviderMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public interface ITwilioMessagingClient
{
    Task<ProviderMessage> SendMessageAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
