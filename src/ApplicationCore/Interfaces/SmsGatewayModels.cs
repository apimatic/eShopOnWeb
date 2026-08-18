using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so, its
/// canonical E.164 form (what the app stores — not whatever the caller typed).
/// </summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);

/// <summary>
/// The outcome of sending, scheduling or reading a message. Carries the provider's identifier and
/// the delivery outcome it owns. When the provider never accepted the message,
/// <see cref="Accepted"/> is false and <see cref="MessageSid"/> is null.
/// </summary>
public record SmsSendResult(
    bool Accepted,
    string? MessageSid,
    string? Status,
    int? ErrorCode,
    string? ErrorMessage)
{
    public static SmsSendResult Failed(string? reason) => new(false, null, null, null, reason);
}

/// <summary>The provider's own record of one message, as returned when listing for reconciliation.</summary>
public record ProviderMessageSummary(
    string Sid,
    string? Status,
    string? From,
    string? To,
    DateTimeOffset? DateSent,
    int? ErrorCode);
