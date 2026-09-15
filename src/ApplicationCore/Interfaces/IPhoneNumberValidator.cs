using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Confirms, with the provider, whether a number is a usable SMS destination and returns the
/// provider's canonical (E.164) form of it. Used when a shopper registers a number so an unusable
/// destination is rejected up front rather than at the moment a message fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a number validation. <see cref="CanonicalNumber"/> is only meaningful when
/// <see cref="IsValid"/> is true.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber);
