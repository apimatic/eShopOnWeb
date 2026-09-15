using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public interface ITwilioGateway
{
    Task<PhoneNumberLookup> ValidatePhoneNumberAsync(string suppliedNumber, CancellationToken cancellationToken);
    Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<TwilioMessage> GetMessageAsync(string sid, CancellationToken cancellationToken);
    Task<TwilioMessage> CancelScheduledMessageAsync(string sid, CancellationToken cancellationToken);
    Task<TwilioMessage> RedactMessageContentAsync(string sid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record PhoneNumberLookup(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

public sealed record TwilioMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? SendAt);

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? providerCode)
        : base("The messaging provider rejected the request.")
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }

    public int StatusCode { get; }
    public int? ProviderCode { get; }
}
