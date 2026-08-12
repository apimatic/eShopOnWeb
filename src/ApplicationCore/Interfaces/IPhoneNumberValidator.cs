using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outcome of validating a phone number with the provider. When valid, <see cref="CanonicalE164"/>
/// carries the provider's own canonical form of the number.
/// </summary>
public record PhoneNumberValidation(bool IsValid, string? CanonicalE164);

/// <summary>
/// Validates that a number is a usable messaging destination and returns its canonical form,
/// using the provider's lookup capability. This is checked when a number is registered, not when a
/// message later fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidation> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);
}
