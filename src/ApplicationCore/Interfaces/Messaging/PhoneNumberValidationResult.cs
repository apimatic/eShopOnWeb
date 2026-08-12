namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

/// <summary>
/// The provider's verdict on a phone number the shopper wants to register: whether it is a usable
/// destination and, if so, its canonical E.164 form.
/// </summary>
public record PhoneNumberValidationResult
{
    /// <summary>True when the provider considers the number a usable destination.</summary>
    public bool IsUsable { get; init; }

    /// <summary>The provider's canonical E.164 form of the number. Present only when usable.</summary>
    public string? CanonicalNumber { get; init; }

    /// <summary>A short reason the number was rejected. Present only when not usable.</summary>
    public string? Reason { get; init; }

    public static PhoneNumberValidationResult Usable(string canonicalNumber) =>
        new() { IsUsable = true, CanonicalNumber = canonicalNumber };

    public static PhoneNumberValidationResult Unusable(string reason) =>
        new() { IsUsable = false, Reason = reason };
}
