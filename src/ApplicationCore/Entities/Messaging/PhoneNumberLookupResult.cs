using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public record PhoneNumberLookupResult(
    bool Valid,
    string? CanonicalPhoneNumber,
    string? NationalFormat,
    string? LineType,
    IReadOnlyList<string> ValidationErrors,
    int? LineTypeErrorCode);
