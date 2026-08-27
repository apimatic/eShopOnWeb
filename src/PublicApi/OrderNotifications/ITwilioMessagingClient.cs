using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public interface ITwilioMessagingClient
{
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken);
    Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken);
    Task<ProviderMessage> FetchMessageAsync(string sid, CancellationToken cancellationToken);
    Task<ProviderMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken);
    Task<ProviderMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken);
}

public sealed record ValidatedPhoneNumber(bool IsValid, string? PhoneNumber,
    IReadOnlyList<string> ValidationErrors);

public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? From,
    string? To,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class TwilioRequestException : Exception
{
    public TwilioRequestException(System.Net.HttpStatusCode statusCode, int? providerCode)
        : base("The messaging provider rejected the request.")
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
    public int? ProviderCode { get; }
}
