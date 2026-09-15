using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class UnavailableSmsNotificationGateway : ISmsNotificationGateway
{
    public string ConfiguredFromNumber => string.Empty;
    public Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken)
        => Task.FromResult(new PhoneLookupResult(false, null, "The messaging provider is not configured."));

    public Task<SmsMessageSnapshot> TrySendAsync(SmsSendRequest request, CancellationToken cancellationToken)
        => Task.FromResult(new SmsMessageSnapshot(null, "failed", null, null, request.Body, null, "The messaging provider is not configured.", null, null, null));

    public Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
        => Task.FromResult<SmsMessageSnapshot?>(null);

    public Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
        => Task.FromResult<SmsMessageSnapshot?>(null);

    public Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
        => Task.FromResult<SmsMessageSnapshot?>(null);

    public Task<IReadOnlyList<SmsMessageSnapshot>> ListFromConfiguredNumberAsync(System.DateTimeOffset from, System.DateTimeOffset to, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SmsMessageSnapshot>>(System.Array.Empty<SmsMessageSnapshot>());
}
