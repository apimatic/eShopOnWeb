using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsMessagingService
{
    Task<ProviderSendResult> SendAsync(CreateProviderMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<ProviderMessage?> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> CancelAsync(string messageSid, CancellationToken cancellationToken = default);
}
