using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed class PhoneNumberLookupResult
{
    public bool IsValid { get; init; }
    public string? CanonicalNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}
