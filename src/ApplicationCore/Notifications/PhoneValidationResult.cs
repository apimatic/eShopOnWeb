namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Outcome of asking the provider whether a number is a usable destination.
/// <see cref="CanonicalNumber"/> is the provider's own E.164 form and is only meaningful when
/// <see cref="IsValid"/> is true.
/// </summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber);
