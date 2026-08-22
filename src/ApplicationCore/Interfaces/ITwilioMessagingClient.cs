using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    string FromNumber { get; }

    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}
