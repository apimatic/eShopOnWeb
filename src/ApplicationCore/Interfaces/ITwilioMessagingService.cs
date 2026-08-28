using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingService
{
    Task<string?> ValidateAndNormalizeAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<TwilioMessageState> SendAsync(string to, string body, CancellationToken cancellationToken);
    Task<TwilioMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<TwilioMessageState> FetchAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessageState> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessageState> RedactAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessageState>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record TwilioMessageState(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? Body);
