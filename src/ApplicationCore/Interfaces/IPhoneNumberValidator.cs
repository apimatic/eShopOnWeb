using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Asks the messaging provider whether a number is a usable destination and
/// returns the provider's canonical form of it.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken ct = default);
}

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = new List<string>();
}
