using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number.</summary>
    public string? CanonicalNumber { get; set; }

    public IReadOnlyList<string> ValidationErrors { get; set; } = new List<string>();
}

/// <summary>
/// Validates a phone number with the messaging provider and returns the
/// provider's canonical form of it.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
