using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record PhoneNumberLookupResult(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);
