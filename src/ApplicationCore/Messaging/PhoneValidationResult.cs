namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// Outcome of validating a phone number with the provider before it is stored.
/// </summary>
/// <param name="IsUsable">Whether the provider considers the number a usable messaging destination.</param>
/// <param name="CanonicalE164">The provider's own canonical (E.164) form of the number when usable; otherwise null.</param>
public record PhoneValidationResult(bool IsUsable, string? CanonicalE164);
