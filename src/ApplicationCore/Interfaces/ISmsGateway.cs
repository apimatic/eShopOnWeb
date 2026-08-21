using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<SmsSendResult> SendImmediateAsync(string to, string body, CancellationToken cancellationToken = default);

    Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default);

    Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken = default);

    Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);

    Task<SmsListResult> ListFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
}

public sealed class SmsListResult
{
    public IReadOnlyList<SmsMessageSnapshot> Messages { get; init; } = Array.Empty<SmsMessageSnapshot>();
    public bool Truncated { get; init; }
}
