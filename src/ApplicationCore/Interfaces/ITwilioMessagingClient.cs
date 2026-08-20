using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class TwilioMessageSnapshot
{
    public string Sid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public string? Direction { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
}

public sealed class TwilioRequestException : Exception
{
    public TwilioRequestException(int statusCode, int? errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public int? ErrorCode { get; }
}
