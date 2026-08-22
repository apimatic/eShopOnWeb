using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessageResult(
    string? Sid,
    string Status,
    string? Body,
    string? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? From,
    string? To);

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }

    Task<TwilioMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageResult>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
