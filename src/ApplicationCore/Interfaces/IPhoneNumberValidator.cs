using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns the provider's canonical form of it,
/// so a number the provider does not consider a usable destination is rejected when it is registered
/// rather than when a message later fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of validating a phone number.
/// </summary>
/// <param name="IsValid">Whether the provider considers the number a usable destination.</param>
/// <param name="CanonicalNumber">The provider's canonical (E.164) form of the number, when valid.</param>
/// <param name="Reason">A PII-free explanation when the number is not usable.</param>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Reason)
{
    public static PhoneNumberValidationResult Valid(string canonicalNumber) => new(true, canonicalNumber, null);
    public static PhoneNumberValidationResult Invalid(string reason) => new(false, null, reason);
}
