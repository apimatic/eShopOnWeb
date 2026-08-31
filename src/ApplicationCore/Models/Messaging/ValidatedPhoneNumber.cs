using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>Result of asking the messaging provider whether a phone number is a usable destination.</summary>
public class ValidatedPhoneNumber
{
    public ValidatedPhoneNumber(bool isValid, string? canonicalNumber, IReadOnlyList<string> validationErrors)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        ValidationErrors = validationErrors;
    }

    public bool IsValid { get; }

    /// <summary>The provider's canonical (E.164) form of the number. Null when invalid.</summary>
    public string? CanonicalNumber { get; }

    public IReadOnlyList<string> ValidationErrors { get; }
}
