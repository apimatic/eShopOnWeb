namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's verdict on a phone number the shopper wants to register: whether it is a usable
/// destination and, when it is, the provider's own canonical (E.164) form of it.
/// </summary>
public class PhoneValidationResult
{
    private PhoneValidationResult(bool isValid, string? canonicalE164, string? reason)
    {
        IsValid = isValid;
        CanonicalE164 = canonicalE164;
        Reason = reason;
    }

    public bool IsValid { get; }

    /// <summary>The provider-canonical E.164 number to store. Only set when <see cref="IsValid"/> is true.</summary>
    public string? CanonicalE164 { get; }

    /// <summary>Why an invalid number was rejected, when the provider says.</summary>
    public string? Reason { get; }

    public static PhoneValidationResult Valid(string canonicalE164) => new(true, canonicalE164, null);

    public static PhoneValidationResult Invalid(string? reason) => new(false, null, reason);
}
