using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<ProviderMessage> SendAsync(OutgoingSms message, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> UpdateAsync(string messageSid, SmsUpdate update, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
