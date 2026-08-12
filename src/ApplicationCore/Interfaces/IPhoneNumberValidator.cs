using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a mobile number with the provider at the moment it is registered, so an unusable
/// destination is rejected up front rather than when a later message fails to go out. Returns the
/// provider's own canonical form of the number to store.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a provider phone-number lookup.</summary>
public class PhoneNumberValidationResult
{
    private PhoneNumberValidationResult(bool isValid, string? canonicalNumber, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        Errors = errors;
    }

    /// <summary>True when the provider considers the number a usable destination.</summary>
    public bool IsValid { get; }

    /// <summary>The provider's canonical E.164 form — what should be stored. Null when invalid.</summary>
    public string? CanonicalNumber { get; }

    /// <summary>Provider-supplied reasons the number was rejected (e.g. TOO_SHORT, NOT_A_NUMBER).</summary>
    public IReadOnlyList<string> Errors { get; }

    public static PhoneNumberValidationResult Valid(string canonicalNumber) =>
        new(true, canonicalNumber, new List<string>());

    public static PhoneNumberValidationResult Invalid(IReadOnlyList<string> errors) =>
        new(false, null, errors);
}
