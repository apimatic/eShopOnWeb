using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record TwilioLookupResult(
    bool Valid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);
