using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>Outcome of asking the provider whether a number is a usable destination.</summary>
public class PhoneNumberValidationResult
{
    public bool IsValid { get; init; }

    /// <summary>The provider's canonical (E.164) form of the number, present only when valid.</summary>
    public string? CanonicalNumber { get; init; }

    /// <summary>Provider-supplied reasons the number is not usable.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}
