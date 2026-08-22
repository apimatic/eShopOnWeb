using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record TwilioMessageRecord(
    string Sid,
    string Status,
    string? Body,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated,
    string? ErrorCode);

public interface ITwilioMessagingClient
{
    Task<TwilioMessageRecord> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageRecord> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageRecord?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageRecord> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageRecord> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageRecord>> ListFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
