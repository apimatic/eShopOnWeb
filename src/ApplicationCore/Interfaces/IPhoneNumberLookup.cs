using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns the
/// provider's canonical form of it.
/// </summary>
public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

public record PhoneNumberLookupResult(bool IsValid, string? CanonicalNumber, string? Error);
