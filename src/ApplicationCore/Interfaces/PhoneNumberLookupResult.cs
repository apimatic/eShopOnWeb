using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? CanonicalPhoneNumber { get; init; }
    public string? NationalFormat { get; init; }
    public string? CountryCode { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = System.Array.Empty<string>();
}
