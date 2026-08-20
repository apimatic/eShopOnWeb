using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }

    Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> UpdateAsync(string messageSid, string? body, string? status, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
