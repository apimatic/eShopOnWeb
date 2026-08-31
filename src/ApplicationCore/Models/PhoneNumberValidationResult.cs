namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class PhoneNumberValidationResult
{
    public bool IsValid { get; }
    public string? CanonicalNumber { get; }
    public string? Error { get; }

    private PhoneNumberValidationResult(bool isValid, string? canonicalNumber, string? error)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        Error = error;
    }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new PhoneNumberValidationResult(true, canonicalNumber, null);

    public static PhoneNumberValidationResult Invalid(string error) =>
        new PhoneNumberValidationResult(false, null, error);
}
