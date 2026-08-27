using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, string? Error);

/// <summary>
/// Validates a phone number with the messaging provider and returns the
/// provider's canonical form of the number.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
