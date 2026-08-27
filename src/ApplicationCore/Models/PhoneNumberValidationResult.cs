namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// Outcome of asking the messaging provider whether a number is a usable destination.
/// </summary>
/// <param name="IsValid">Whether the provider considers the number a usable destination.</param>
/// <param name="CanonicalNumber">The provider's canonical (E.164) form of the number; set when valid.</param>
/// <param name="Error">A caller-safe reason when invalid. Never contains the number itself.</param>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Error)
{
    public static PhoneNumberValidationResult Valid(string canonicalNumber) => new(true, canonicalNumber, null);

    public static PhoneNumberValidationResult Invalid(string error) => new(false, null, error);
}
