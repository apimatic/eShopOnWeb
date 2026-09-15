using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Twilio.Models;

/// <summary>
/// The Lookups v2 response (schema <c>LookupResponse</c>). <see cref="Valid"/> says whether the number
/// is a usable destination; <see cref="PhoneNumber"/> is its canonical E.164 form when it is.
/// </summary>
public class TwilioLookupResponse
{
    public bool Valid { get; set; }
    public string? PhoneNumber { get; set; }
    public string? NationalFormat { get; set; }
    public string? CountryCode { get; set; }
    public List<string>? ValidationErrors { get; set; }
}
