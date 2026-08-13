namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The outcome of asking the messaging provider whether a number is a usable destination and,
/// if so, what its canonical (E.164) form is.
/// </summary>
public class PhoneNumberValidationResult
{
    private PhoneNumberValidationResult(bool isValid, string? canonicalNumber, string? reason)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        Reason = reason;
    }

    public bool IsValid { get; }

    /// <summary>The provider's canonical E.164 form of the number, when valid.</summary>
    public string? CanonicalNumber { get; }

    /// <summary>A caller-safe reason the number was rejected, when invalid.</summary>
    public string? Reason { get; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new(true, canonicalNumber, null);

    public static PhoneNumberValidationResult Invalid(string reason) =>
        new(false, null, reason);
}
