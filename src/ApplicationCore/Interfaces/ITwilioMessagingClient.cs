using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string ConfiguredFromNumber { get; }

    Task<ProviderMessageState> CreateMessageAsync(OutboundSmsRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessageState> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageState> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageState> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessageState>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
