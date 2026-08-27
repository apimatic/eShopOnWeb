using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioMessagingClient
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string to, string content, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string providerMessageId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalPhoneNumber, IReadOnlyList<string> Errors);

public sealed record ProviderMessage(
    string Id,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string operation, int? statusCode = null, int? errorCode = null)
        : base($"Twilio {operation} failed{(statusCode is null ? string.Empty : $" with HTTP {statusCode}")}.")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int? StatusCode { get; }
    public int? ErrorCode { get; }
}
