using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

public interface ITwilioGateway
{
    Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken);
    Task<TwilioMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken);
    Task<TwilioMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken);
    Task<TwilioMessage> FetchMessageAsync(string sid, CancellationToken cancellationToken);
    Task<TwilioMessage> CancelMessageAsync(string sid, CancellationToken cancellationToken);
    Task<TwilioMessage> RedactMessageAsync(string sid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record ValidatedPhoneNumber(string CanonicalNumber, bool IsValid);

public sealed record TwilioMessage(
    string Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? providerCode)
        : base($"Twilio request failed with HTTP status {httpStatus}.")
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }

    public int HttpStatus { get; }
    public int? ProviderCode { get; }
}
