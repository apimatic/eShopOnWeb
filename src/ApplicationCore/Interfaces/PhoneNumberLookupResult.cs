using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public sealed class PhoneNumberLookupResult
{
    public bool IsValid { get; init; }
    public string? CanonicalPhoneNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];
}
