using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates and canonicalises a phone number with the messaging provider before it is
/// ever stored, so an unusable destination is rejected at registration time rather than
/// at the moment a message fails to go out.
/// </summary>
public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a provider phone-number lookup.</summary>
public class PhoneNumberLookupResult
{
    private PhoneNumberLookupResult(bool isValid, string? canonicalNumber, string? reason)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        Reason = reason;
    }

    /// <summary>Whether the provider considers this a usable destination.</summary>
    public bool IsValid { get; }

    /// <summary>The provider's canonical E.164 form of the number (only when valid).</summary>
    public string? CanonicalNumber { get; }

    /// <summary>A human-readable reason when the number is not usable.</summary>
    public string? Reason { get; }

    public static PhoneNumberLookupResult Valid(string canonicalNumber) =>
        new(true, canonicalNumber, null);

    public static PhoneNumberLookupResult Invalid(string reason) =>
        new(false, null, reason);
}
