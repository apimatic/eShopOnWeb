using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    bool IsConfigured { get; }

    string FromNumber { get; }

    Task<SmsSendResult> SendAsync(string toE164, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> CancelAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
