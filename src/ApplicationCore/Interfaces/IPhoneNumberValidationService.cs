using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberValidationResult
{
    public PhoneNumberValidationResult(bool isValid, string? canonicalNumber, string? error)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        Error = error;
    }

    public bool IsValid { get; }
    public string? CanonicalNumber { get; }
    public string? Error { get; }
}

/// <summary>
/// Validates a phone number with the messaging provider and returns the provider's
/// canonical form of the number.
/// </summary>
public interface IPhoneNumberValidationService
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
