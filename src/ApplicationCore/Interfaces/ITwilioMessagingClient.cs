using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class TwilioMessageRecord
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}

public class SendMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<TwilioMessageRecord> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageRecord?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageRecord> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageRecord> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageRecord>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
