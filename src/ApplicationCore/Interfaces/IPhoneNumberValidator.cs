using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number against the messaging provider and returns the
/// provider's canonical form of it. Throws <see cref="Exceptions.MessagingException"/>
/// when the provider itself cannot be queried.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken ct = default);
}

public class PhoneNumberValidationResult
{
    public PhoneNumberValidationResult(bool isValid, string? canonicalNumber, string? failureReason)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        FailureReason = failureReason;
    }

    public bool IsValid { get; }

    /// <summary>The provider's canonical (E.164) form of the number; null when invalid.</summary>
    public string? CanonicalNumber { get; }

    public string? FailureReason { get; }
}
