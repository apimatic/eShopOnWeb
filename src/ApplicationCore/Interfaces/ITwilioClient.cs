using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioClient
{
    Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberValidation(bool IsValid, string? CanonicalNumber);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    string? Body);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(int httpStatusCode, int? providerErrorCode = null)
        : base($"Twilio request failed with HTTP status {httpStatusCode}.")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
}
