using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ISmsProvider
{
    Task<PhoneNumberValidation> ValidateDestinationAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);
    Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? SentAt,
    string? Body);

public sealed class SmsProviderException : Exception
{
    public SmsProviderException(string operation, int? providerErrorCode = null, Exception? innerException = null)
        : base($"The SMS provider rejected or could not complete the {operation} operation.", innerException)
    {
        ProviderErrorCode = providerErrorCode;
    }

    public int? ProviderErrorCode { get; }
}
