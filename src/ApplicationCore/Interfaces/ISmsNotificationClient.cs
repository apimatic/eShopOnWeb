using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsNotificationClient
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken);

    Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);

    Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken);

    Task<SmsReconciliationPage> ListSentFromAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken);
}
