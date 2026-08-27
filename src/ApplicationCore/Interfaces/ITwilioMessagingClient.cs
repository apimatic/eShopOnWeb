using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
