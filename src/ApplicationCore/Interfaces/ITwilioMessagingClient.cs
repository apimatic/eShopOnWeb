using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }

    Task<ProviderMessageResult> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessageResult>> ListSentFromAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
