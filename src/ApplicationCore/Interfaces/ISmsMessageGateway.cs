using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsMessageGateway
{
    string ConfiguredFromNumber { get; }

    Task<SmsDispatchResult> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
