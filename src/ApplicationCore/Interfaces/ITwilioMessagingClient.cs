using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    int? ErrorCode,
    string? ErrorMessage);

public sealed record SendSmsRequest(string To, string Body, DateTimeOffset? SendAt);

public interface ITwilioMessagingClient
{
    string GetConfiguredFromNumber();
    Task<ProviderMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
