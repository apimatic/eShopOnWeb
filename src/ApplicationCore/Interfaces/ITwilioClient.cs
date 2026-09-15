using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioClient
{
    Task<TwilioPhoneLookup> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);
    Task<TwilioMessageRecord> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken);
    Task<TwilioMessageRecord> FetchMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessageRecord> CancelMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<TwilioMessageRecord> RedactMessageAsync(string messageSid, CancellationToken cancellationToken);
    Task<IReadOnlyList<TwilioMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed record TwilioPhoneLookup(bool Valid, string? PhoneNumber);

public sealed record TwilioMessageRecord(
    string Sid,
    string? Body,
    string? From,
    string? To,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(int? statusCode, string message) : base(message) => StatusCode = statusCode;
    public int? StatusCode { get; }
}
