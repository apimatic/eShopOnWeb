using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }
    Task<TwilioMessageSnapshot> SendAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class TwilioMessageSnapshot
{
    public string Sid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Body { get; init; }
    public DateTimeOffset? DateSent { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? From { get; init; }
}

public class TwilioApiException : Exception
{
    public TwilioApiException(int? statusCode, int? errorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int? StatusCode { get; }
    public int? ErrorCode { get; }
}
