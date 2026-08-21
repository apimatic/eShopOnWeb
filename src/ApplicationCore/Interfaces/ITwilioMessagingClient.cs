using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class TwilioSendMessageRequest
{
    public required string To { get; init; }
    public required string Body { get; init; }
    public DateTimeOffset? SendAt { get; init; }
}

public class TwilioMessageSnapshot
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}

public class TwilioSendResult
{
    public bool Accepted { get; init; }
    public TwilioMessageSnapshot? Message { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorStatus { get; init; }
}

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }
    Task<TwilioSendResult> SendAsync(TwilioSendMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot?> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
