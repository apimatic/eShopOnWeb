using System;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// Outcome of validating a destination number with the provider. <see cref="IsUsableDestination"/>
/// reflects the provider's own judgement; <see cref="CanonicalE164"/> is the provider's canonical
/// form of the number, to be stored in place of whatever the caller typed.
/// </summary>
public sealed record PhoneValidationResult(bool IsUsableDestination, string? CanonicalE164, string? Reason);

/// <summary>
/// Provider state for a single message: its identifier and current delivery outcome, plus any
/// provider error detail. Carries no destination number so it can flow freely without leaking one.
/// </summary>
public sealed record SmsSendResult(
    string? ProviderMessageSid,
    string Status,
    int? ErrorCode,
    string? ErrorMessage,
    DateTimeOffset? DateSent);

/// <summary>
/// One entry from the provider's own list of messages, for reconciliation. Deliberately excludes
/// the destination number.
/// </summary>
public sealed record ProviderMessageRecord(
    string Sid,
    string Status,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
