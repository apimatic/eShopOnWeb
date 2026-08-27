using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberValidationResult(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Validates a phone number with the provider and returns the provider's canonical form.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
