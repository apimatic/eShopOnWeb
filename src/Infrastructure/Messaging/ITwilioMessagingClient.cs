using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public interface ITwilioMessagingClient
{
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
