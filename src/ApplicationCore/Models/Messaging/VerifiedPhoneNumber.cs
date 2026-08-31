namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>
/// A phone number the messaging provider considers a usable destination,
/// in the provider's canonical form.
/// </summary>
public record VerifiedPhoneNumber(string CanonicalNumber);
