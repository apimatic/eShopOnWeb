using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public sealed class PhoneNumberLookupResult
{
    public PhoneNumberLookupResult(bool isValid, string? canonicalNumber, string? countryCode, string? validationReason)
    {
        IsValid = isValid;
        CanonicalNumber = canonicalNumber;
        CountryCode = countryCode;
        ValidationReason = validationReason;
    }

    public bool IsValid { get; }
    public string? CanonicalNumber { get; }
    public string? CountryCode { get; }
    public string? ValidationReason { get; }
}
