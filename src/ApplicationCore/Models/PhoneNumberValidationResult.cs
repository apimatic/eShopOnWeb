namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable SMS destination,
/// and if so the provider's canonical (E.164) form of it.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Error);
