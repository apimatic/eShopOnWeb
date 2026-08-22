using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessageClient
{
    Task<TwilioMessageSnapshot?> CreateMessageAsync(TwilioCreateMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot?> UpdateMessageAsync(string messageSid, TwilioUpdateMessageRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class TwilioCreateMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public string? From { get; init; }
    public string? MessagingServiceSid { get; init; }
    public string? ScheduleType { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public sealed class TwilioUpdateMessageRequest
{
    public string? Body { get; init; }
    public string? Status { get; init; }
}

public sealed class TwilioMessageSnapshot
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Uri { get; init; }
    public string? MessagingServiceSid { get; init; }
}
