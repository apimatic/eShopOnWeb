using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioGateway
{
    Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageContentAsync(string providerMessageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string? From,
    string? To,
    string Status,
    string? Body,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated,
    int? ErrorCode);

public sealed class TwilioRequestException : Exception
{
    public TwilioRequestException(string operation, int statusCode, int? providerCode)
        : base($"Twilio {operation} failed with HTTP status {statusCode}.")
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
