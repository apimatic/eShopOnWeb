using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<TwilioMessageSnapshot> SendAsync(TwilioSendMessageRequest request, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
