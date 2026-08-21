using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class PhoneNumberLookupResult
{
    public bool Valid { get; init; }
    public string? CanonicalNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}
