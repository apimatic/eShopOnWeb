using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneNumberValidation> ValidateDestinationAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> Errors);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public class SmsProviderException : Exception
{
    public SmsProviderException(string message, int? providerErrorCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
