using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessageResource(
    string? Sid,
    string? Status,
    int? ErrorCode,
    string? Body,
    string? From,
    string? To,
    string? DateSent,
    string? DateCreated);

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<TwilioMessageResource> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageResource> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageResource> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResource> CancelAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResource> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageResource>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
