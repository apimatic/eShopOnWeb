namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// Outcome of asking the provider to validate and canonicalize a phone number.
/// </summary>
/// <param name="IsValid">Whether the provider considers the number a usable destination.</param>
/// <param name="CanonicalNumber">The provider's canonical (E.164) form, when valid.</param>
public record PhoneLookupResult(bool IsValid, string? CanonicalNumber);
