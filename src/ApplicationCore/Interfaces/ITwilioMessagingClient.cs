using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessageResult(
    string Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    int? ErrorCode,
    string? DateSent,
    string? DateCreated);

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<TwilioMessageResult> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageResult>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
}
