using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class TwilioMessageSnapshot
{
    public string Sid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? Body { get; init; }
    public DateTimeOffset? DateCreated { get; init; }
    public DateTimeOffset? DateSent { get; init; }
}

public sealed class TwilioMessagingException : Exception
{
    public TwilioMessagingException(string message) : base(message)
    {
    }

    public TwilioMessagingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
