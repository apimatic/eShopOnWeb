namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;

/// <summary>
/// The result of asking the provider whether a number is a usable destination. When
/// <see cref="Valid"/> is true, <see cref="PhoneNumber"/> holds the provider's own canonical
/// E.164 form of the number (which is what should be stored), not whatever the caller typed.
/// </summary>
public record PhoneLookupResult(bool Valid, string? PhoneNumber);
