using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsMessageSnapshot(
    string? ProviderMessageSid,
    string Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public record SendSmsRequest(string To, string Body, DateTimeOffset? SendAt = null);

public interface ISmsGateway
{
    Task<SmsMessageSnapshot> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> GetAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    string ConfiguredFromNumber { get; }

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
