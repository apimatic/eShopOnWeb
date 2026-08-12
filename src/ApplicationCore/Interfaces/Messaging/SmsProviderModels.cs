using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>Outcome of validating/canonicalising a phone number with the provider.</summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalE164);

/// <summary>The provider's acknowledgement of a send or schedule request.</summary>
public record SentMessage(string Sid, string Status, int? ErrorCode);

/// <summary>The provider's current view of a single message.</summary>
public record MessageState(string Status, int? ErrorCode);

/// <summary>One message as it appears in the provider's own records, for reconciliation.</summary>
public record ProviderMessage(string Sid, string Status, int? ErrorCode, string To, DateTimeOffset? DateSent);
