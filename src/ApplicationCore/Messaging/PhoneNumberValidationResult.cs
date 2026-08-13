namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so,
/// its canonical E.164 form.
/// </summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical E.164 representation of the number. Present only when valid.</summary>
    public string? CanonicalNumber { get; init; }

    public static PhoneNumberValidationResult Invalid() => new() { IsValid = false };

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new() { IsValid = true, CanonicalNumber = canonicalNumber };
}
