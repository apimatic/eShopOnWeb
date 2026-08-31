namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The provider's verdict on whether a phone number is a usable destination,
/// along with the provider's canonical (E.164) form of the number.
/// </summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? ValidationError { get; set; }
}
