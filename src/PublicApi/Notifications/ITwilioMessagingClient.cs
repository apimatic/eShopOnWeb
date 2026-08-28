using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface ITwilioMessagingClient
{
    Task<PhoneNumberLookup> LookupPhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken);
    Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberLookup(bool Valid, string? PhoneNumber, IReadOnlyList<string> ValidationErrors);

public sealed record TwilioMessage(
    string Sid,
    string Status,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatusCode, int? providerErrorCode)
        : base("The messaging provider rejected the request.")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
}
