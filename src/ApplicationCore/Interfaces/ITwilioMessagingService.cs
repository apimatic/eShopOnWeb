using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessage(
    string Sid,
    string? Status,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    string? DateSent,
    string? DateCreated);

public record SendMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public interface ITwilioMessagingService
{
    Task<TwilioMessage> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessage>> ListFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    string FromNumber { get; }
}
