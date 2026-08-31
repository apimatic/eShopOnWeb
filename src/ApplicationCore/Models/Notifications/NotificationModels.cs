using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Notifications;

public record PhoneNumberValidationResult(
    bool IsValid,
    string? E164Number,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

public record SmsSendResult(
    string? MessageSid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage);

public record SmsMessageDetails(
    string MessageSid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

public record ProviderMessageRecord(
    string MessageSid,
    string? To,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateSent,
    DateTimeOffset? DateCreated);
