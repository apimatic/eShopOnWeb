using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool valid, string? canonicalPhoneNumber, string? nationalFormat, IReadOnlyList<string> validationErrors)
    {
        Valid = valid;
        CanonicalPhoneNumber = canonicalPhoneNumber;
        NationalFormat = nationalFormat;
        ValidationErrors = validationErrors;
    }

    public bool Valid { get; }
    public string? CanonicalPhoneNumber { get; }
    public string? NationalFormat { get; }
    public IReadOnlyList<string> ValidationErrors { get; }
}
