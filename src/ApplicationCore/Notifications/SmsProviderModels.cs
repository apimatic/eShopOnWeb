using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Outcome of validating/normalising a phone number with the provider's lookup.</summary>
public record PhoneNumberValidationResult(
    bool IsValid,
    string? E164,
    string? NationalFormat,
    string? CountryCode,
    IReadOnlyList<string> ValidationErrors);

/// <summary>Outcome of creating (sending or scheduling) a message with the provider.</summary>
public record SmsSendResult(string Sid, string Status, int? ErrorCode);

/// <summary>The provider's own record of a message, as returned when listing/fetching.</summary>
public record ProviderMessage(
    string Sid,
    string? Status,
    string? From,
    string? To,
    string? Direction,
    DateTimeOffset? DateSent,
    int? ErrorCode);
