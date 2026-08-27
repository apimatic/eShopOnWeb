using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Messaging;

/// <summary>Outcome of asking the provider whether a number is a usable destination.</summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical (E.164) form of the number.</summary>
    public string? CanonicalNumber { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = new List<string>();
}
