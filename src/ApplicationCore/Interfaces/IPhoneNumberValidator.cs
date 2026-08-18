using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outcome of validating a caller-supplied phone number with the provider's lookup capability.
/// <see cref="E164Number"/> is the provider's canonical form and is only meaningful when
/// <see cref="IsValid"/> is true.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? E164Number, IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Validates and canonicalises a phone number using the provider, so an unusable destination is
/// rejected at registration rather than at the moment a message would fail to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);
}
