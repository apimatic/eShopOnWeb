using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number against the provider and returns the provider's own canonical form, so a
/// number the provider does not consider a usable destination is rejected up front rather than at the
/// moment a message fails to go out.
/// </summary>
public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a provider lookup. <see cref="CanonicalE164"/> is the provider's E.164 form of the number
/// and is what should be stored when <see cref="IsValid"/> is true.
/// </summary>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> ValidationErrors);
