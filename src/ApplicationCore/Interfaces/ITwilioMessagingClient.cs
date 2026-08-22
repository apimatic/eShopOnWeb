using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record SmsMessageResult(
    string Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public interface ITwilioMessagingClient
{
    Task<SmsMessageResult> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<SmsMessageResult> FetchAsync(string sid, CancellationToken cancellationToken = default);

    Task<SmsMessageResult> CancelAsync(string sid, CancellationToken cancellationToken = default);

    Task<SmsMessageResult> RedactBodyAsync(string sid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageResult>> ListFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
