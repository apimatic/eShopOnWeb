using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public record PhoneNumberLookupResult(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Asks the provider whether a phone number is a usable destination and returns
/// the provider's canonical form of the number.
/// </summary>
public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}
