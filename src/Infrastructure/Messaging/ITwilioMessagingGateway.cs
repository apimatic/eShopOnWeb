using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public interface ITwilioMessagingGateway
{
    Task<PhoneValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    string? From,
    string? To,
    string? Body,
    string? MessagingServiceSid,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);
