using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    string SendingNumber { get; }

    Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsSendResult> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsSendResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        CancellationToken cancellationToken);
}
