using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsNotificationGateway
{
    string FromNumber { get; }

    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);

    Task<SmsSendAttempt> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<ProviderMessage?> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
