using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the messaging provider and returns the provider's canonical
/// (E.164) form. Backed by the provider's lookup API — the authoritative judge of whether a
/// number is a usable destination.
/// </summary>
public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a provider phone-number lookup.
/// </summary>
/// <param name="IsValid">True when the provider considers the number a valid, assignable destination.</param>
/// <param name="CanonicalE164">The provider's canonical E.164 form of the number (only meaningful when valid).</param>
/// <param name="ValidationErrors">Provider reasons the number is not valid, if any.</param>
public record PhoneNumberLookupResult(bool IsValid, string? CanonicalE164, IReadOnlyList<string> ValidationErrors);
