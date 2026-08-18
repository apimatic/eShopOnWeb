namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outcome of asking the provider whether a number is a usable destination, and for its canonical form.
/// </summary>
public class PhoneNumberValidationResult
{
    private PhoneNumberValidationResult(bool isValid, string? canonicalNumber, string? failureReason)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        FailureReason = failureReason;
    }

    public bool IsValid { get; }

    /// <summary>The provider's canonical E.164 form of the number, when valid.</summary>
    public string? CanonicalNumber { get; }

    /// <summary>Why the number was rejected, when invalid.</summary>
    public string? FailureReason { get; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new(true, canonicalNumber, null);

    public static PhoneNumberValidationResult Invalid(string failureReason) =>
        new(false, null, failureReason);
}
