using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record ProviderSmsMessage(
    string Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    string? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public interface IOrderSmsGateway
{
    string ConfiguredFromNumber { get; }

    Task<ProviderSmsMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<ProviderSmsMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderSmsMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderSmsMessage?> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderSmsMessage?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderSmsMessage>> ListFromConfiguredSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
