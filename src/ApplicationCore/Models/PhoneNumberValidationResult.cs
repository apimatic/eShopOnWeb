using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }

    /// <summary>
    /// The provider's canonical (E.164) form of the number. Only set when valid.
    /// </summary>
    public string? CanonicalNumber { get; set; }

    public IReadOnlyList<string> ValidationErrors { get; set; } = new List<string>();
}
