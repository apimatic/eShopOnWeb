using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SendProviderMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt);

public record UpdateProviderMessageRequest(
    string? Body,
    string? Status);

public record ProviderMessage(
    string Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    int? ErrorCode,
    string? ErrorMessage);

public interface ITwilioMessagingClient
{
    Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default);
    Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        UpdateProviderMessageRequest request,
        CancellationToken cancellationToken = default);

    string ConfiguredFromNumber { get; }
}
