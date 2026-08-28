using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string number, CancellationToken cancellationToken = default);
}

public sealed record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> Errors);

public interface ITextMessagingProvider
{
    Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default);
    Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<ProviderMessage> RedactAsync(string providerMessageSid, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed record ProviderMessage(
    string Sid,
    string? From,
    string? To,
    string? Body,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, int? providerCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderCode = providerCode;
    }

    public int? ProviderCode { get; }
}
