using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

public sealed class PhoneNumberLookup
{
    public required bool Valid { get; init; }
    public string? CanonicalPhoneNumber { get; init; }
    public IReadOnlyList<string> ValidationErrors { get; init; } = Array.Empty<string>();
}
