namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Sms;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so,
/// its canonical E.164 form.
/// </summary>
public class PhoneNumberLookupResult
{
    /// <summary>True when the provider considers the number a valid, reachable destination.</summary>
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical E.164 representation of the number (present when valid).</summary>
    public string? CanonicalNumber { get; init; }

    /// <summary>Provider-reported reasons the number is not usable (present when invalid).</summary>
    public string? ValidationError { get; init; }
}
