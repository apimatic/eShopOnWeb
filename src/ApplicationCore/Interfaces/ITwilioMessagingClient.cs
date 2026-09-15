using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<ProviderMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<ProviderMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task<ProviderMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessageSnapshot> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessageSnapshot>> ListFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    string ConfiguredFromNumber { get; }
}
