using System;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Outcome of a phone-number validation. <see cref="CanonicalNumber"/> is the provider's E.164 form.</summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, string? NationalFormat);

/// <summary>Provider identifier and initial status for a message that was accepted (immediate or scheduled).</summary>
public record SmsDispatchResult(string MessageSid, string? Status);

/// <summary>A message's current provider-side delivery outcome.</summary>
public record SmsStatusResult(string? Status, string? ErrorCode);

/// <summary>
/// One row of the provider's own record of a sent message. Deliberately carries no phone number,
/// so a reconciliation report never leaks a shopper's contact detail.
/// </summary>
public record ProviderMessage(string Sid, string? Status, DateTimeOffset? DateSentUtc, string? ErrorCode);
