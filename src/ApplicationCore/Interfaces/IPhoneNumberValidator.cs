using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns its canonical form.
/// Backed by the provider's phone-number lookup capability.
/// </summary>
public interface IPhoneNumberValidator
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// The provider's verdict on a phone number: whether it is a usable destination and, if so,
/// its canonical E.164 form.
/// </summary>
public class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool isValid, string? canonicalNumber, IReadOnlyList<string> validationErrors)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        ValidationErrors = validationErrors;
    }

    /// <summary>True when the provider considers the number a valid, assignable destination.</summary>
    public bool IsValid { get; }

    /// <summary>The provider's canonical E.164 representation of the number (null when invalid).</summary>
    public string? CanonicalNumber { get; }

    /// <summary>Provider-supplied reasons the number is not valid (e.g. TOO_LONG, INVALID_COUNTRY_CODE).</summary>
    public IReadOnlyList<string> ValidationErrors { get; }
}
