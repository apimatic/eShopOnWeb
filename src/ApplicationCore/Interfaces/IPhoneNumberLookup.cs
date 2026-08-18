using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Validates a phone number with the provider and returns its canonical form. Implemented
/// against Twilio Lookup v2 (a different host than the messaging API).
/// </summary>
public interface IPhoneNumberLookup
{
    Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outcome of a lookup. <see cref="Valid"/> reflects the provider's judgement that the number is a
/// usable destination; <see cref="E164"/> is the provider's canonical form of it.
/// </summary>
public record PhoneNumberLookupResult(bool Valid, string? E164, string? NationalFormat);
