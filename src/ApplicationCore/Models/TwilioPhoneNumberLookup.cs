using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// eShop's view of a Twilio Lookups v2 phone number lookup.
/// </summary>
public class TwilioPhoneNumberLookup
{
    public bool IsValid { get; set; }
    public string? CanonicalPhoneNumber { get; set; }
    public string? NationalFormat { get; set; }
    public string? CountryCode { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = new List<string>();
}
