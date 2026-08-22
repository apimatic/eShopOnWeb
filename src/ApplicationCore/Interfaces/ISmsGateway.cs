using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    string FromNumber { get; }
    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public record SmsSendRequest(string To, string Body, DateTimeOffset? SendAt = null);

public record SmsMessageSnapshot(
    string? Sid,
    string? Status,
    string? Body,
    string? From,
    string? To,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
