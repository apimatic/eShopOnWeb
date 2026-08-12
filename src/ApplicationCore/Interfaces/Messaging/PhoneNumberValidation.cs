namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// Result of asking the provider whether a number is a usable destination and what its canonical
/// (E.164) form is.
/// </summary>
/// <param name="IsValid">True when the provider considers the number a usable destination.</param>
/// <param name="CanonicalNumber">The provider's canonical E.164 form, when the number is valid.</param>
public record PhoneNumberValidation(bool IsValid, string? CanonicalNumber);
