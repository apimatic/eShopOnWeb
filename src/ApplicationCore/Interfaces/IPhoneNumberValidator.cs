using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates and canonicalises a phone number with the provider (its Lookup API), so a number
/// the provider does not consider a usable destination is rejected up front, and what is stored
/// is the provider's own canonical form.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of a lookup. When <see cref="IsValid"/> is true, <see cref="CanonicalNumber"/>
/// carries the provider's E.164 form of the number.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber, IReadOnlyList<string> Errors);
