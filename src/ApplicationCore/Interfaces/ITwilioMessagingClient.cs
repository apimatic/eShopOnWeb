using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TwilioMessageSnapshot>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
