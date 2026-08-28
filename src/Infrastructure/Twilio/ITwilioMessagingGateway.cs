using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public interface ITwilioMessagingGateway
{
    Task<string> ValidateAndCanonicalizeAsync(string phoneNumber, CancellationToken ct);
    Task<ProviderMessageState> SendAsync(string to, string body, CancellationToken ct);
    Task<ProviderMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct);
    Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken ct);
    Task<ProviderMessageState> CancelAsync(string providerMessageSid, CancellationToken ct);
    Task<ProviderMessageState> RedactAsync(string providerMessageSid, CancellationToken ct);
    Task<IReadOnlyList<ProviderMessageState>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
