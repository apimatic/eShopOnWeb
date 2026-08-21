using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }

    Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> CancelAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
