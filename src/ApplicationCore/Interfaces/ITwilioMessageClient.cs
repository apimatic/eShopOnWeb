using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessageClient
{
    string FromNumber { get; }

    Task<TwilioMessageSnapshot> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public class TwilioMessageSnapshot
{
    public string Sid { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? Body { get; init; }
    public string? To { get; init; }
    public string? From { get; init; }
    public int? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}
