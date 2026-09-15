using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so, its
/// canonical (E.164) form.
/// </summary>
public sealed record PhoneValidationResult(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);
