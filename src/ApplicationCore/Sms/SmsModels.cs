using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

public sealed class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool valid, string? phoneNumber, string? nationalFormat, IReadOnlyList<string> validationErrors)
    {
        Valid = valid;
        PhoneNumber = phoneNumber;
        NationalFormat = nationalFormat;
        ValidationErrors = validationErrors;
    }

    public bool Valid { get; }
    public string? PhoneNumber { get; }
    public string? NationalFormat { get; }
    public IReadOnlyList<string> ValidationErrors { get; }
}

public sealed class SendSmsRequest
{
    public SendSmsRequest(string to, string body, DateTimeOffset? sendAt = null)
    {
        To = to;
        Body = body;
        SendAt = sendAt;
    }

    public string To { get; }
    public string Body { get; }
    public DateTimeOffset? SendAt { get; }
}

public sealed class ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }
    public int? ErrorCode { get; init; }
    public string? Body { get; init; }
    public string? From { get; init; }
    public string? DateSent { get; init; }
    public string? DateCreated { get; init; }
}

public sealed class SmsSendAttempt
{
    public SmsSendAttempt(bool accepted, ProviderMessage? message, int? errorCode)
    {
        Accepted = accepted;
        Message = message;
        ErrorCode = errorCode;
    }

    public bool Accepted { get; }
    public ProviderMessage? Message { get; }
    public int? ErrorCode { get; }
}
