using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber, string? CountryCode);

/// <summary>
/// Validates a phone number with the provider and returns the provider's canonical
/// form of it, so unusable destinations are rejected before anything is stored.
/// </summary>
public interface IPhoneNumberLookupService
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
