using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? NationalFormat { get; set; }
    public IReadOnlyList<string> ValidationErrors { get; set; } = new List<string>();
}

/// <summary>
/// Validates a caller-supplied phone number through the messaging provider and
/// returns the provider's canonical form of the number.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, string? countryCode = null, CancellationToken cancellationToken = default);
}
