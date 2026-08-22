using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<SmsMessageSnapshot?> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists messages the provider sent from this application's configured From number
    /// over the given range. Pagination is followed until the range is exhausted.
    /// </summary>
    Task<IReadOnlyList<SmsMessageSnapshot>> ListSentByConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
