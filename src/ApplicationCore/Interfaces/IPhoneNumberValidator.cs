using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Checks with the provider whether a number is a usable SMS destination and, when it is, hands back
/// the provider's own canonical E.164 form of it. Used when a shopper registers a number so a bad
/// number is rejected up front rather than at the moment a message fails to go out.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of a validation. When <see cref="IsValid"/> is true, <see cref="CanonicalE164"/> holds
/// the provider's canonical form. Otherwise <see cref="Errors"/> explains why it was rejected.
/// </summary>
public record PhoneNumberValidationResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> Errors)
{
    public static PhoneNumberValidationResult Valid(string canonicalE164) =>
        new(true, canonicalE164, new List<string>());

    public static PhoneNumberValidationResult Invalid(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}
