using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record PhoneNumberLookupResult(
    bool IsValid,
    string? CanonicalNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

public record SendMessageRequest(
    string To,
    string Body,
    DateTimeOffset? SendAt = null);

public record ProviderMessageResult(
    string? Sid,
    string Status,
    string? Body,
    string? From,
    string? To,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateCreated,
    DateTimeOffset? DateSent);

public class TwilioClientException : Exception
{
    public TwilioClientException(string message, int? statusCode = null, int? errorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int? StatusCode { get; }
    public int? ErrorCode { get; }
}
