using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessageSnapshot(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? Body,
    string? DateCreated,
    string? DateSent);

public record CreateTwilioMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public interface ITwilioMessagingClient
{
    Task<TwilioMessageSnapshot> CreateMessageAsync(CreateTwilioMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
