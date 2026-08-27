using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns the provider's
/// canonical form of the number.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public class PhoneNumberValidationResult
{
    public bool IsValid { get; set; }
    public string? CanonicalNumber { get; set; }
    public string? Error { get; set; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new PhoneNumberValidationResult { IsValid = true, CanonicalNumber = canonicalNumber };

    public static PhoneNumberValidationResult Invalid(string error) =>
        new PhoneNumberValidationResult { IsValid = false, Error = error };
}
