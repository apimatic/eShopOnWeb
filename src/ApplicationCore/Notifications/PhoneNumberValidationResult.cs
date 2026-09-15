using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// The outcome of asking the messaging provider whether a number is a usable destination and what its
/// canonical form is.
/// </summary>
public class PhoneNumberValidationResult
{
    public PhoneNumberValidationResult(bool isValid, string? canonicalNumber, IReadOnlyList<string>? errors = null)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        Errors = errors ?? new List<string>();
    }

    /// <summary>Whether the provider considers the number a usable destination.</summary>
    public bool IsValid { get; }

    /// <summary>The provider's canonical (E.164) form of the number. Present when <see cref="IsValid"/> is true.</summary>
    public string? CanonicalNumber { get; }

    /// <summary>Provider-reported validation errors, when the number is not usable.</summary>
    public IReadOnlyList<string> Errors { get; }
}
