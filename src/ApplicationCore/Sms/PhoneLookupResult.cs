namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// Result of asking the provider whether a number is a usable destination and, if so, its
/// canonical E.164 form.
/// </summary>
public record PhoneLookupResult(bool IsValid, string? CanonicalNumber);
