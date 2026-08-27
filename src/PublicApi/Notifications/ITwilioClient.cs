using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface ITwilioClient
{
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken);
    Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<TwilioMessage> GetMessageAsync(string sid, CancellationToken cancellationToken);
    Task<TwilioMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken);
    Task<TwilioMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ValidatedPhoneNumber(bool IsValid, string? CanonicalNumber);

public sealed record TwilioMessage(
    string Sid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class TwilioRequestException : Exception
{
    public TwilioRequestException(string operation, int httpStatusCode, int? providerErrorCode = null)
        : base($"Twilio {operation} failed (HTTP {httpStatusCode}, provider code {providerErrorCode?.ToString() ?? "none"}).")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
}

public sealed class TwilioConfigurationException : Exception
{
    public TwilioConfigurationException(string setting)
        : base($"Twilio configuration is missing required setting '{setting}'.") { }
}
