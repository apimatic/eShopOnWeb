using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public record PhoneLookupResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> ValidationErrors);
