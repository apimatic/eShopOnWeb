using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>Outcome of validating/canonicalizing a phone number with the provider.</summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> Errors)
{
    public static PhoneNumberValidationResult Valid(string canonicalE164) =>
        new(true, canonicalE164, Array.Empty<string>());

    public static PhoneNumberValidationResult Invalid(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}

/// <summary>What the provider returned when it accepted (or refused to accept) a message we sent or scheduled.</summary>
public record SmsSendResult(string? Sid, string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>The provider's current delivery outcome for a previously sent message.</summary>
public record SmsDeliveryState(string? Status, int? ErrorCode, string? ErrorMessage);

/// <summary>
/// One entry in the provider's own record of messages, used to reconcile against what eShop believes it sent.
/// </summary>
public record ProviderMessageRecord(
    string Sid,
    string? Status,
    string? To,
    string? From,
    DateTimeOffset? DateSent,
    int? ErrorCode,
    string? ErrorMessage);
