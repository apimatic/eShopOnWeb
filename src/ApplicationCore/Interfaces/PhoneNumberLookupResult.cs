namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool isValid, string? canonicalPhoneNumber)
    {
        IsValid = isValid;
        CanonicalPhoneNumber = canonicalPhoneNumber;
    }

    public bool IsValid { get; }
    public string? CanonicalPhoneNumber { get; }
}
