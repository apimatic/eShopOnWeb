using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidation(bool IsValid, string? CanonicalPhoneNumber,
    IReadOnlyCollection<string> ValidationErrors);

public record ProviderMessage(string Id, string Status, int? ErrorCode,
    DateTimeOffset? DateCreated, DateTimeOffset? DateSent);

public class MessageProviderException : Exception
{
    public MessageProviderException(string operation, int? providerErrorCode = null, Exception? innerException = null)
        : base($"The messaging provider could not complete {operation}.", innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}

public interface IMessageProvider
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> GetAsync(string providerMessageId,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string providerMessageId,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactAsync(string providerMessageId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProviderMessage>> ListApplicationMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken = default);
}
