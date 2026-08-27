using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ProviderMessage(
    string? Sid,
    string? Status,
    string? Body,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? From);

public interface ITwilioMessagingClient
{
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);
    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
