using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Outcome of validating/canonicalizing a phone number with the provider.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164, string? Reason);

/// <summary>The provider-owned state of a message this app sent or read back.</summary>
public record SentSms(
    string? ProviderSid,
    string? Status,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>One message as the provider reports it, used for reconciliation.</summary>
public record ProviderMessageRecord(
    string Sid,
    string? Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent);
