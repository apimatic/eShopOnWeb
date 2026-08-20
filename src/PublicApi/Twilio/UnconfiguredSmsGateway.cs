using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

internal sealed class UnconfiguredSmsGateway : ISmsGateway
{
    public string SendingNumber => string.Empty;

    public Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
        => Task.FromResult(new SmsLookupResult(false, null, "SMS notifications are not configured."));

    public Task<SmsMessageSnapshot> SendImmediateAsync(string to, string body, CancellationToken cancellationToken)
        => Task.FromResult(Failed());

    public Task<SmsMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => Task.FromResult(Failed());

    public Task<SmsMessageSnapshot> FetchAsync(string sid, CancellationToken cancellationToken)
        => Task.FromResult(Failed());

    public Task<SmsMessageSnapshot> CancelScheduledAsync(string sid, CancellationToken cancellationToken)
        => Task.FromResult(Failed());

    public Task<SmsMessageSnapshot> RedactBodyAsync(string sid, CancellationToken cancellationToken)
        => Task.FromResult(Failed());

    public Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAppAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SmsMessageSnapshot>>(Array.Empty<SmsMessageSnapshot>());

    private static SmsMessageSnapshot Failed() =>
        new(false, null, "failed", null, "SMS notifications are not configured.", null, null, null, null, null);
}
