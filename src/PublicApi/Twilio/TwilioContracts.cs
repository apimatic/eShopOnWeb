using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.PublicApi.Twilio;

public interface ITwilioLookupClient
{
    Task<TwilioPhoneLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken);
}

public interface ITwilioMessagingClient
{
    Task<TwilioMessage> CreateAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessage> RedactAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record TwilioPhoneLookup(bool Valid, string? PhoneNumber);

public sealed record TwilioMessage(
    string Sid,
    string? From,
    string? To,
    string Status,
    string? Body,
    int? ErrorCode,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateUpdated);

public sealed class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? providerCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }

    public int HttpStatus { get; }
    public int? ProviderCode { get; }
}
