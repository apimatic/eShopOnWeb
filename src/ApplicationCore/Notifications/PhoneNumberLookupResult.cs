using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Result of a provider phone-number lookup. Mirrors the fields of the Lookups v2
/// <c>LookupResponse</c> schema that this integration relies on.
/// </summary>
public class PhoneNumberLookupResult
{
    /// <summary>Whether the provider considers this a usable destination.</summary>
    public bool Valid { get; init; }

    /// <summary>The provider's canonical E.164 form of the number (present whether or not it is valid).</summary>
    public string? PhoneNumber { get; init; }

    /// <summary>Provider validation error codes (e.g. TOO_LONG, INVALID_COUNTRY_CODE) when not valid.</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = new List<string>();
}
