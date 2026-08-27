using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(
    bool IsValid,
    string? CanonicalNumber,
    string? NationalFormat,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Validates a phone number with the provider and returns the provider's canonical form.
/// </summary>
public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
