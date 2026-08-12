using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Result of validating a phone number against the provider. When <see cref="IsValid"/> is
/// true, <see cref="CanonicalNumber"/> is the provider's own canonical E.164 form to store.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Reason);

/// <summary>
/// Result of submitting (sending or scheduling) a message to the provider.
/// </summary>
public record SentMessageResult(string? Sid, string Status, string? ErrorCode, DateTimeOffset? DateSent);

/// <summary>
/// The provider's current view of a single message.
/// </summary>
public record MessageDeliveryState(string Status, string? ErrorCode, DateTimeOffset? DateSent);

/// <summary>
/// One message as the provider records it, used for reconciliation. The body is intentionally
/// not carried here — reconciliation lines up identifiers and delivery outcomes, not content.
/// </summary>
public record ProviderMessage(string Sid, string? To, string? From, string Status, string? ErrorCode, DateTimeOffset? DateSent);
