namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so, the
/// provider's own canonical (E.164) form of it &ndash; which is what gets stored, not the raw input.
/// </summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical E.164 form. Present only when <see cref="IsValid"/> is true.</summary>
    public string? CanonicalNumber { get; init; }

    /// <summary>A short, non-PII reason the number was rejected (e.g. "TOO_SHORT").</summary>
    public string? Reason { get; init; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new() { IsValid = true, CanonicalNumber = canonicalNumber };

    public static PhoneNumberValidationResult Invalid(string? reason) =>
        new() { IsValid = false, Reason = reason };
}
