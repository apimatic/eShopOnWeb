using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken ct);

    Task<SmsMessageSnapshot> SendAsync(string toCanonicalNumber, string body, CancellationToken ct);

    Task<SmsMessageSnapshot> ScheduleAsync(string toCanonicalNumber, string body, DateTimeOffset sendAt, CancellationToken ct);

    Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken ct);

    Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken ct);

    Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken ct);

    string FromNumber { get; }

    Task<SmsMessageList> ListFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
