using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<ProviderMessage> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        string? body,
        string? status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
