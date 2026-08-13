using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Result of a provider lookup. When <see cref="IsValid"/> is true, <see cref="CanonicalNumber"/>
/// holds the provider's canonical E.164 form of the number.
/// </summary>
public record PhoneValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> Errors);

/// <summary>
/// Validates a phone number with the provider and returns its canonical form, so a number the
/// provider does not consider a usable destination is rejected at registration time rather
/// than when a message later fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
