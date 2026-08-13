using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Outcome of validating a destination number with the provider.</summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical (E.164) form of the number, when valid.</summary>
    public string? CanonicalNumber { get; init; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new() { IsValid = true, CanonicalNumber = canonicalNumber };

    public static PhoneNumberValidationResult Invalid() => new() { IsValid = false };
}

/// <summary>Result of handing a message to the provider (immediate or scheduled).</summary>
public class SmsDispatchResult
{
    /// <summary>The provider's message identifier (SID).</summary>
    public string Sid { get; init; } = default!;

    /// <summary>The provider's delivery status at the moment it accepted the message.</summary>
    public string? Status { get; init; }
}

/// <summary>
/// The provider's own record of a message, as returned when listing messages for reconciliation.
/// Deliberately omits the destination number so the report never carries a shopper's number.
/// </summary>
public class ProviderMessage
{
    public string? Sid { get; init; }
    public string? Status { get; init; }

    /// <summary>The sending number (this application's configured From number), not a shopper number.</summary>
    public string? From { get; init; }

    /// <summary>The provider's raw date-sent string.</summary>
    public string? DateSent { get; init; }
}
