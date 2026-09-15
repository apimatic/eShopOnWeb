using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IPhoneNumberValidator
{
    Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, string? countryCode,
        CancellationToken cancellationToken = default);
}

public sealed record PhoneNumberValidationResult(bool IsValid, string? CanonicalNumber,
    string? CountryCode, string? Reason);
