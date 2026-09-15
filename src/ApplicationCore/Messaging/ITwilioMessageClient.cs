using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public interface ITwilioMessageClient
{
    Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default);
}
