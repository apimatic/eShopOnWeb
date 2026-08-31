using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public record SmsSendResult(string? MessageSid, string Status, int? ErrorCode);

public record SmsMessageState(
    string MessageSid,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public record SmsMessageRecord(
    string MessageSid,
    string? To,
    string? From,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);

public record PhoneNumberValidationResult(
    bool IsValid,
    string? E164Number,
    string? NationalFormat,
    IReadOnlyList<string> Errors)
{
    public static PhoneNumberValidationResult Invalid(IReadOnlyList<string> errors)
        => new(false, null, null, errors);
}
