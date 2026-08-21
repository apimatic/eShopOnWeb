using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsGateway
{
    Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default);
    Task<SmsMessage> FetchAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessage> CancelAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<SmsMessage> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SmsMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);
    string ConfiguredFromNumber { get; }
}
