using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class PhoneNumberValidationResult
{
    public PhoneNumberValidationResult(bool isValid, string? canonicalNumber, IReadOnlyList<string> validationErrors)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        ValidationErrors = validationErrors;
    }

    public bool IsValid { get; }
    public string? CanonicalNumber { get; }
    public IReadOnlyList<string> ValidationErrors { get; }
}

/// <summary>
/// Validates a phone number with the messaging provider and returns the
/// provider's canonical form of the number.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
