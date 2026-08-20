using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<LookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<GatewayResult> SendImmediateAsync(string to, string body, CancellationToken cancellationToken);
    Task<GatewayResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<GatewayResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);
    Task<GatewayResult> FetchAsync(string providerSid, CancellationToken cancellationToken);
    Task<GatewayResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);
    Task<ProviderMessageList> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    string FromNumber { get; }
}
