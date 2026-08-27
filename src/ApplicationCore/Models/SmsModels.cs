using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>Outcome of asking the provider whether a number is a usable destination.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? FailureReason);

/// <summary>A message the provider accepted (immediately or scheduled).</summary>
public record SmsSendResult(string MessageSid, string? Status);

/// <summary>The provider's current record of a single message.</summary>
public record SmsMessageStatusResult(string? Status, int? ErrorCode, string? ErrorMessage, DateTimeOffset? DateSent);

/// <summary>The provider's record of a message, as returned by its message list.</summary>
public record ProviderSmsRecord(
    string MessageSid,
    string? To,
    string? From,
    string? Status,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>The provider's message list for a range, plus the sending number queried and whether paging hit its cap.</summary>
public record ProviderSmsListResult(
    IReadOnlyList<ProviderSmsRecord> Messages,
    string FromNumber,
    bool Truncated);
