using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so, its
/// canonical E.164 form.
/// </summary>
public class PhoneNumberLookupResult
{
    public required bool Valid { get; init; }

    // The provider's canonical E.164 form of the number. Present when the number could be parsed.
    public string? PhoneNumber { get; init; }

    public IReadOnlyList<string> ValidationErrors { get; init; } = new List<string>();
}
