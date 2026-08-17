using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>Outcome of validating a phone number with the provider.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalE164, string? Reason)
{
    public static PhoneValidationResult Valid(string canonicalE164) => new(true, canonicalE164, null);
    public static PhoneValidationResult Invalid(string? reason) => new(false, null, reason);
}

/// <summary>The provider's acknowledgement of a message it accepted for sending or scheduling.</summary>
public record SentMessage(string ProviderMessageSid, string Status);

/// <summary>The provider's current delivery outcome for a message.</summary>
public record MessageDeliveryStatus(string Status, int? ErrorCode, string? ErrorMessage);

/// <summary>A message as the provider records it, used for reconciliation.</summary>
public record ProviderMessage(string? Sid, string? Status, string? From, string? To, DateTimeOffset? DateSent);
