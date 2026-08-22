using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed record TwilioMessageResult(
    string Sid,
    string Status,
    string? Body,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    int? ErrorCode);

public interface ITwilioMessagingClient
{
    Task<TwilioMessageResult> SendAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageResult>> ListFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
