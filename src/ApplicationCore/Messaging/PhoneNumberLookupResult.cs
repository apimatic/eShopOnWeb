using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalE164,
    string? NationalFormat,
    string? CountryCode,
    string? LineType,
    int? LineTypeErrorCode,
    IReadOnlyList<string> ValidationErrors);
