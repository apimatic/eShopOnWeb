using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class TwilioMessageResult
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public string? Direction { get; init; }
    public string? From { get; init; }
}

public class CreateTwilioMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public class TwilioMessageListRequest
{
    public required string From { get; init; }
    public required DateTimeOffset DateSentAfter { get; init; }
    public required DateTimeOffset DateSentBefore { get; init; }
}

public interface ITwilioMessagingClient
{
    Task<TwilioMessageResult> CreateMessageAsync(CreateTwilioMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageResult> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageResult> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageResult>> ListMessagesFromAsync(TwilioMessageListRequest request, CancellationToken cancellationToken = default);
}
