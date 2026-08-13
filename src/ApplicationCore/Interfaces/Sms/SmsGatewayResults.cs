using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

/// <summary>Result of validating a phone number with the provider's Lookup API.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);

/// <summary>Result of asking the provider to send or schedule a message.</summary>
public record SentSmsMessage(string Sid, string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current record of a message's delivery outcome.</summary>
public record SmsMessageState(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>One entry from the provider's own list of messages, used for reconciliation.</summary>
public record ProviderMessageRecord(
    string Sid,
    string? To,
    string? From,
    string Status,
    int? ErrorCode,
    DateTimeOffset? DateSent);
